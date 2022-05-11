using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using AutoUpdaterDotNET.Properties;

namespace AutoUpdaterDotNET
{
    public class DownloadUpdateClass
    {
        private readonly UpdateInfoEventArgs _args;

        private string _tempFile;

        private MyWebClient _webClient;

        private DateTime _startedAt;

        public string SpeedInformation;
        public string Size;
        public int Progress;

        public delegate void OnDownloadFinishedEventHandler(bool Success);

        public event OnDownloadFinishedEventHandler DownloadFinishedEvent;

        public delegate void OnProgressChangedEventHandler(object sender);
        public event OnProgressChangedEventHandler OnProgressChanged;


        public DownloadUpdateClass(UpdateInfoEventArgs args, bool useExternDownload)
        {
            _args = args;
            
            if (string.IsNullOrEmpty(AutoUpdater.DownloadPath))
            {
                _tempFile = Path.GetTempFileName();
            }
            else
            {
                _tempFile = Path.Combine(AutoUpdater.DownloadPath, $"{Guid.NewGuid().ToString()}.tmp");
                if (!Directory.Exists(AutoUpdater.DownloadPath))
                {
                    Directory.CreateDirectory(AutoUpdater.DownloadPath);
                }
            }

            
            if(!useExternDownload)
            {
                var uri = new Uri(_args.DownloadURL);
                _webClient = AutoUpdater.GetWebClient(uri, AutoUpdater.BasicAuthDownload);
                _webClient.DownloadFileAsync(uri, _tempFile);
                _webClient.DownloadProgressChanged += OnDownloadProgressChanged;
                _webClient.DownloadFileCompleted += WebClientOnDownloadFileCompleted;
            }
        }

        private void OnDownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            if (_startedAt == default(DateTime))
            {
                _startedAt = DateTime.Now;
            }
            else
            {
                var timeSpan = DateTime.Now - _startedAt;
                long totalSeconds = (long)timeSpan.TotalSeconds;
                if (totalSeconds > 0)
                {
                    var bytesPerSecond = e.BytesReceived / totalSeconds;
                    SpeedInformation = string.Format(Resources.DownloadSpeedMessage, BytesToString(bytesPerSecond));
                }
            }

            Size = $@"{BytesToString(e.BytesReceived)} / {BytesToString(e.TotalBytesToReceive)}";
            Progress = e.ProgressPercentage;

            OnProgressChanged(this);
        }

        public void OnExternDownloadFinished()
        {
            WebClientOnDownloadFileCompleted(this, new AsyncCompletedEventArgs(null, false, null));
        }

        private void WebClientOnDownloadFileCompleted(object sender, AsyncCompletedEventArgs asyncCompletedEventArgs)
        {
            if (asyncCompletedEventArgs.Cancelled)
            {
                return;
            }

            try
            {
                if (asyncCompletedEventArgs.Error != null)
                {
                    throw asyncCompletedEventArgs.Error;
                }

                if (_args.CheckSum != null)
                {
                    CompareChecksum(_tempFile, _args.CheckSum);
                }

                string fileName = "";
                string tempPath = "";

                if(_webClient != null)
                {
                    ContentDisposition contentDisposition = null;
                    if (!String.IsNullOrWhiteSpace(_webClient.ResponseHeaders?["Content-Disposition"]))
                    {
                        contentDisposition = new ContentDisposition(_webClient.ResponseHeaders["Content-Disposition"]);
                    }

                    fileName = string.IsNullOrEmpty(contentDisposition?.FileName)
                        ? Path.GetFileName(_webClient.ResponseUri.LocalPath)
                        : contentDisposition.FileName;

                    tempPath =
                    Path.Combine(
                        string.IsNullOrEmpty(AutoUpdater.DownloadPath) ? Path.GetTempPath() : AutoUpdater.DownloadPath,
                        fileName);

                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }

                    File.Move(_tempFile, tempPath);
                }
                else
                {
                    fileName = _args.DownloadURL.Substring(_args.DownloadURL.LastIndexOf('/') + 1);
                    tempPath = Path.Combine(AutoUpdater.DownloadPath, fileName);
                }                              

                string installerArgs = null;
                if (!string.IsNullOrEmpty(_args.InstallerArgs))
                {
                    installerArgs = _args.InstallerArgs.Replace("%path%",
                        Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName));
                }

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true,
                    Arguments = installerArgs ?? string.Empty
                };

                var extension = Path.GetExtension(tempPath);
                if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    string installerPath = Path.Combine(Path.GetDirectoryName(tempPath) ?? throw new InvalidOperationException(), "ZipExtractor.exe");

                    File.WriteAllBytes(installerPath, Resources.ZipExtractor);

                    string executablePath = Process.GetCurrentProcess().MainModule?.FileName;
                    string extractionPath = Path.GetDirectoryName(executablePath);

                    if (!string.IsNullOrEmpty(AutoUpdater.InstallationPath) &&
                        Directory.Exists(AutoUpdater.InstallationPath))
                    {
                        extractionPath = AutoUpdater.InstallationPath;
                    }

                    StringBuilder arguments =
                        new StringBuilder($"\"{tempPath}\" \"{extractionPath}\" \"{executablePath}\"");

                    if (AutoUpdater.ClearAppDirectory)
                    {
                        arguments.Append(" -c");
                    }

                    string[] args = Environment.GetCommandLineArgs();
                    for (int i = 1; i < args.Length; i++)
                    {
                        if (i.Equals(1))
                        {
                            arguments.Append(" \"");
                        }

                        arguments.Append(args[i]);
                        arguments.Append(i.Equals(args.Length - 1) ? "\"" : " ");
                    }

                    processStartInfo = new ProcessStartInfo
                    {
                        FileName = installerPath,
                        UseShellExecute = true,
                        Arguments = arguments.ToString()
                    };
                }
                else if (extension.Equals(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    processStartInfo = new ProcessStartInfo
                    {
                        FileName = "msiexec",
                        Arguments = $"/i \"{tempPath}\"",
                    };
                    if (!string.IsNullOrEmpty(installerArgs))
                    {
                        processStartInfo.Arguments += " " + installerArgs;
                    }
                }

                if (AutoUpdater.RunUpdateAsAdmin)
                {
                    processStartInfo.Verb = "runas";
                }

                if(!extension.Equals(".pwr", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        Process.Start(processStartInfo);
                    }
                    catch (Win32Exception exception)
                    {
                        if (exception.NativeErrorCode == 1223)
                        {
                            _webClient = null;
                        }
                        else
                        {
                            throw;
                        }
                    }
                }              
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, e.GetType().ToString(), MessageBoxButtons.OK, MessageBoxIcon.Error);
                _webClient = null;
            }
            finally
            {
                DownloadFinishedEvent?.Invoke(true);
            }
        }

        private static string BytesToString(long byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            if (byteCount == 0)
                return "0" + suf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return $"{(Math.Sign(byteCount) * num).ToString(CultureInfo.InvariantCulture)} {suf[place]}";
        }

        private static void CompareChecksum(string fileName, CheckSum checksum)
        {
            using (var hashAlgorithm =
                HashAlgorithm.Create(
                    string.IsNullOrEmpty(checksum.HashingAlgorithm) ? "MD5" : checksum.HashingAlgorithm))
            {
                using (var stream = File.OpenRead(fileName))
                {
                    if (hashAlgorithm != null)
                    {
                        var hash = hashAlgorithm.ComputeHash(stream);
                        var fileChecksum = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();

                        if (fileChecksum == checksum.Value.ToLower()) return;

                        throw new Exception(Resources.FileIntegrityCheckFailedMessage);
                    }

                    throw new Exception(Resources.HashAlgorithmNotSupportedMessage);
                }
            }
        }
    }
}
