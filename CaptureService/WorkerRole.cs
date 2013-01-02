using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.StorageClient;
using System.IO;

namespace CaptureService
{
    public class WorkerRole : RoleEntryPoint
    {
        public override void Run()
        {
            var account = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue("Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString"));
            var container = account.CreateCloudBlobClient().GetContainerReference("screenshots");
            container.CreateIfNotExist();
            container.SetPermissions(new BlobContainerPermissions { PublicAccess = BlobContainerPublicAccessType.Blob });

            var queue = account.CreateCloudQueueClient().GetQueueReference("incoming");
            queue.CreateIfNotExist();

            var outputPath = Path.Combine(RoleEnvironment.GetLocalResource("LocalOutput").RootPath, "output.jpg");

            while (true)
            {
                var msg = queue.GetMessage();
                if (msg != null)
                {
                    if (msg.DequeueCount < 3)
                    {
                        var url = msg.AsString;
                        var filename = string.Format("{0}.jpg", url.Replace(".", "")).Replace(@"https://", "").Replace(@"http://", "");
                        if (!Exists(container.GetBlobReference(filename)))
                        {
                            using (var proc = new Process()
                            {
                                StartInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("RoleRoot") + @"\\approot\wkhtmltoimage.exe",
                                        string.Format(@"--disable-smart-width --width {0} --height {1} --quality {2} --enable-plugins --javascript-delay {3} {4} {5}",
                                            RoleEnvironment.GetConfigurationSettingValue("width"),
                                            RoleEnvironment.GetConfigurationSettingValue("height"),
                                            RoleEnvironment.GetConfigurationSettingValue("quality"),
                                            (int.Parse(RoleEnvironment.GetConfigurationSettingValue("delaySeconds")) * 1000),
                                            url,
                                            outputPath))
                                    {
                                        //UseShellExecute = false,
                                        //ErrorDialog = false,
                                        //RedirectStandardError = true
                                        CreateNoWindow = true
                                    }
                            })
                            {
                                try
                                {
                                    proc.EnableRaisingEvents = true;
                                    proc.Exited += new EventHandler((object sender, EventArgs e) =>
                                    {
                                        if (File.Exists(outputPath))
                                        {
                                            var blob = container.GetBlobReference(filename);
                                            blob.Properties.ContentType = "image/jpg";
                                            blob.UploadFile(outputPath);
                                            File.Delete(outputPath);
                                        }
                                    });
                                    proc.Start();
                                    proc.WaitForExit();
                                }
                                catch (Exception ex)
                                {
                                    Trace.WriteLine(ex.Message);
                                    Trace.WriteLine(ex.StackTrace);
                                }
                            }
                        }
                    }
                    queue.DeleteMessage(msg);
                }
                else
                {
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                }
            }
        }

        public override bool OnStart()
        {
            // Set the maximum number of concurrent connections 
            ServicePointManager.DefaultConnectionLimit = 12;

            // For information on handling configuration changes
            // see the MSDN topic at http://go.microsoft.com/fwlink/?LinkId=166357.

            return base.OnStart();
        }

        public bool Exists(CloudBlob blob)
        {
            try
            {
                blob.FetchAttributes();
                return true;
            }
            catch (StorageClientException e)
            {
                if (e.ErrorCode == StorageErrorCode.ResourceNotFound)
                {
                    return false;
                }
                else
                {
                    throw;
                }
            }
        }

    }
}
