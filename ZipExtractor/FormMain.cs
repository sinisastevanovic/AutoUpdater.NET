using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ZipExtractor.Properties;

namespace ZipExtractor
{
    public enum EButlerOutputType
    {
        log,
        progress,
        error,
        result
    }

    public class ButlerOutputBase
    {
        public EButlerOutputType Type { get; set; }
        public int Time { get; set; }
    }

    public class ButlerMessage : ButlerOutputBase
    {
        public string Message { get; set; }
    }

    public class ButlerOutputProgress : ButlerOutputBase
    {
        public double Progress { get; set; }
        public float Eta { get; set; }
        public int Bps { get; set; }
    }

    public partial class FormMain : Form
    {
        private const int MaxRetries = 2;
        private BackgroundWorker _backgroundWorker;
        private readonly StringBuilder _logBuilder = new();
        private int CurrentPatchIndex = 0;
        private string[] PatchFiles;
        private string executablePath = "";
        private string extractionPath = "";
        private string stagingDir = "";
        private string commandLineArgs = "";

        public FormMain()
        {
            InitializeComponent();
        }

        private void FormMain_Shown(object sender, EventArgs e)
        {
            _logBuilder.AppendLine(DateTime.Now.ToString("F"));
            _logBuilder.AppendLine();
            _logBuilder.AppendLine("ZipExtractor started with following command line arguments.");

            string[] args = Environment.GetCommandLineArgs();
            for (var index = 0; index < args.Length; index++)
            {
                var arg = args[index];
                _logBuilder.AppendLine($"[{index}] {arg}");
            }

            _logBuilder.AppendLine();

            if (args.Length >= 4)
            {
                string zipPath = args[1];
                extractionPath = args[2];
                executablePath = args[3];
                bool clearAppDirectory = (args.Length > 4 && args[4] == "-c") || (args.Length > 5 && args[5] == "-c");
                bool patchMode = (args.Length > 4 && args[4] == "-p") || (args.Length > 5 && args[5] == "-p");
                commandLineArgs = "";// = args.Length > 5 ? args[5] : string.Empty;

                if (patchMode && !zipPath.EndsWith(".pwr"))
                {
                    _logBuilder.AppendLine("Error: Update File is not .pwr, but patch mode is enabled");
                    return;
                }
                
                if (!patchMode && !zipPath.EndsWith(".zip"))
                {
                    _logBuilder.AppendLine("Error: Update File is not .zip and patch mode is disabled");
                    return;
                }


                // Extract all the files.
                if (!patchMode)
                {
                    _backgroundWorker = new BackgroundWorker
                    {
                        WorkerReportsProgress = true,
                        WorkerSupportsCancellation = true
                    };

                    _backgroundWorker.DoWork += (_, eventArgs) =>
                    {
                        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executablePath)))
                        {
                            try
                            {
                                if (process.MainModule is { FileName: { } } && process.MainModule.FileName.Equals(executablePath))
                                {
                                    _logBuilder.AppendLine("Waiting for application process to exit...");

                                    _backgroundWorker.ReportProgress(0, "Waiting for application to exit...");
                                    process.WaitForExit();
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.WriteLine(exception.Message);
                            }
                        }

                        _logBuilder.AppendLine($"BackgroundWorker started successfully. PatchMode is {patchMode}");

                        // Ensures that the last character on the extraction path
                        // is the directory separator char.
                        // Without this, a malicious zip file could try to traverse outside of the expected
                        // extraction path.
                        if (!extractionPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                        {
                            extractionPath += Path.DirectorySeparatorChar;
                        }

                        var archive = ZipFile.OpenRead(zipPath);

                        var entries = archive.Entries;

                        try
                        {
                            int progress = 0;

                            if (clearAppDirectory)
                            {
                                _logBuilder.AppendLine($"Removing all files and folders from {extractionPath}.");
                                DirectoryInfo directoryInfo = new DirectoryInfo(extractionPath);

                                foreach (FileInfo file in directoryInfo.GetFiles())
                                {
                                    _logBuilder.AppendLine($"Removing a file located at {file.FullName}.");
                                    _backgroundWorker.ReportProgress(0, string.Format(Resources.Removing, file.FullName));
                                    file.Delete();
                                }
                                foreach (DirectoryInfo directory in directoryInfo.GetDirectories())
                                {
                                    _logBuilder.AppendLine($"Removing a directory located at {directory.FullName} and all its contents.");
                                    _backgroundWorker.ReportProgress(0, string.Format(Resources.Removing, directory.FullName));
                                    directory.Delete(true);
                                }
                            }

                            _logBuilder.AppendLine($"Found total of {entries.Count} files and folders inside the zip file.");

                            for (var index = 0; index < entries.Count; index++)
                            {
                                if (_backgroundWorker.CancellationPending)
                                {
                                    eventArgs.Cancel = true;
                                    break;
                                }

                                var entry = entries[index];

                                string currentFile = string.Format(Resources.CurrentFileExtracting, entry.FullName);
                                _backgroundWorker.ReportProgress(progress, currentFile);
                                int retries = 0;
                                bool notCopied = true;
                                while (notCopied)
                                {
                                    string filePath = String.Empty;
                                    try
                                    {
                                        filePath = Path.Combine(extractionPath, entry.FullName);
                                        if (!entry.IsDirectory())
                                        {
                                            var parentDirectory = Path.GetDirectoryName(filePath);
                                            if (!Directory.Exists(parentDirectory))
                                            {
                                                Directory.CreateDirectory(parentDirectory);
                                            }
                                            entry.ExtractToFile(filePath, true);
                                        }
                                        notCopied = false;
                                    }
                                    catch (IOException exception)
                                    {
                                        const int errorSharingViolation = 0x20;
                                        const int errorLockViolation = 0x21;
                                        var errorCode = Marshal.GetHRForException(exception) & 0x0000FFFF;
                                        if (errorCode is errorSharingViolation or errorLockViolation)
                                        {
                                            retries++;
                                            if (retries > MaxRetries)
                                            {
                                                throw;
                                            }

                                            List<Process> lockingProcesses = null;
                                            if (Environment.OSVersion.Version.Major >= 6 && retries >= 2)
                                            {
                                                try
                                                {
                                                    lockingProcesses = FileUtil.WhoIsLocking(filePath);
                                                }
                                                catch (Exception)
                                                {
                                                    // ignored
                                                }
                                            }

                                            if (lockingProcesses == null)
                                            {
                                                Thread.Sleep(5000);
                                            }
                                            else
                                            {
                                                foreach (var lockingProcess in lockingProcesses)
                                                {
                                                    var dialogResult = MessageBox.Show(
                                                        string.Format(Resources.FileStillInUseMessage,
                                                            lockingProcess.ProcessName, filePath),
                                                        Resources.FileStillInUseCaption,
                                                        MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
                                                    if (dialogResult == DialogResult.Cancel)
                                                    {
                                                        throw;
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            throw;
                                        }
                                    }
                                }

                                progress = (index + 1) * 100 / entries.Count;
                                _backgroundWorker.ReportProgress(progress, currentFile);

                                _logBuilder.AppendLine($"{currentFile} [{progress}%]");
                            }
                        }
                        finally
                        {
                            archive.Dispose();
                        }
                    };

                    _backgroundWorker.ProgressChanged += (_, eventArgs) =>
                    {
                        progressBar.Value = eventArgs.ProgressPercentage;
                        textBoxInformation.Text = eventArgs.UserState?.ToString();
                        if (textBoxInformation.Text != null)
                        {
                            textBoxInformation.SelectionStart = textBoxInformation.Text.Length;
                            textBoxInformation.SelectionLength = 0;
                        }
                    };

                    _backgroundWorker.RunWorkerCompleted += (_, eventArgs) =>
                    {
                        try
                        {
                            if (eventArgs.Error != null)
                            {
                                throw eventArgs.Error;
                            }

                            if (!eventArgs.Cancelled)
                            {
                                textBoxInformation.Text = @"Finished";
                                try
                                {
                                    ProcessStartInfo processStartInfo = new ProcessStartInfo(executablePath);
                                    if (!string.IsNullOrEmpty(commandLineArgs))
                                    {
                                        processStartInfo.Arguments = commandLineArgs;
                                    }

                                    Process.Start(processStartInfo);

                                    _logBuilder.AppendLine("Successfully launched the updated application.");
                                }
                                catch (Win32Exception exception)
                                {
                                    if (exception.NativeErrorCode != 1223)
                                    {
                                        throw;
                                    }
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            _logBuilder.AppendLine();
                            _logBuilder.AppendLine(exception.ToString());

                            MessageBox.Show(exception.Message, exception.GetType().ToString(),
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        finally
                        {
                            _logBuilder.AppendLine();
                            Application.Exit();
                        }
                    };

                    _backgroundWorker.RunWorkerAsync();
                }
                else
                {
                    foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executablePath)))
                    {
                        try
                        {
                            if (process.MainModule is { FileName: { } } && process.MainModule.FileName.Equals(executablePath))
                            {
                                _logBuilder.AppendLine("Waiting for application process to exit...");

                                textBoxInformation.Text = "Waiting for application to exit...";
                                if (textBoxInformation.Text != null)
                                {
                                    textBoxInformation.SelectionStart = textBoxInformation.Text.Length;
                                    textBoxInformation.SelectionLength = 0;
                                }
                                process.WaitForExit();
                            }
                        }
                        catch (Exception exception)
                        {
                            Debug.WriteLine(exception.Message);
                            _logBuilder.AppendLine(exception.Message);
                        }
                    }

                    PatchFiles = zipPath.Split(',');
                    DirectoryInfo dirInfo = new FileInfo(PatchFiles[0]).Directory;
                    stagingDir = dirInfo.FullName + @"\staging";
                    InstallNextPatch();
                }

            }
        }

        private void InstallNextPatch()
        {
            if (CurrentPatchIndex < PatchFiles.Length)
            {
                ApplyPatch(PatchFiles[CurrentPatchIndex], extractionPath, stagingDir);

                CurrentPatchIndex++;
            }
            else
            {
                try
                {

                    textBoxInformation.Text = @"Finished";
                    try
                    {
                        ProcessStartInfo processStartInfo = new ProcessStartInfo(executablePath);
                        if (!string.IsNullOrEmpty(commandLineArgs))
                        {
                            processStartInfo.Arguments = commandLineArgs;
                        }

                        Process.Start(processStartInfo);

                        _logBuilder.AppendLine("Successfully launched the updated application.");
                    }
                    catch (Win32Exception exception)
                    {
                        if (exception.NativeErrorCode != 1223)
                        {
                            throw;
                        }
                    }
                }
                catch (Exception exception)
                {
                    _logBuilder.AppendLine();
                    _logBuilder.AppendLine(exception.ToString());

                    MessageBox.Show(exception.Message, exception.GetType().ToString(),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    _logBuilder.AppendLine();
                    Application.Exit();
                }
            }
        }

        private void ApplyPatch(string patchFilePath, string appDir, string stagingDir)
        {
            if (!File.Exists(patchFilePath) || !Directory.Exists(appDir))
            {
                _logBuilder.AppendLine(String.Format("Patch file not found or Directory does not exist: \n  Patchfile: {Path} \n    AppDir: {appdir}", patchFilePath, appDir));
                return;
            }

            _logBuilder.AppendLine($"Applying patch {patchFilePath} in {appDir}. StagingDir: {stagingDir}");

            if (Directory.Exists(stagingDir))
            {
                DirectoryInfo dir = new DirectoryInfo(stagingDir);
                foreach (var file in dir.GetFiles())
                {
                    file.Delete();
                }
            }

            string applyCommand = string.Format("apply --staging-dir=\"{0}\" \"{1}\" \"{2}\"", stagingDir, patchFilePath, appDir);
            ExecuteButlerCommand(applyCommand);
            return;
        }

        private void ExecuteButlerCommand(string command)
        {
            ProcessStartInfo cmdStartInfo = new ProcessStartInfo();
            cmdStartInfo.FileName = "butler.exe";
            cmdStartInfo.RedirectStandardOutput = true;
            cmdStartInfo.RedirectStandardError = true;
            cmdStartInfo.Arguments = command + " --json";
            cmdStartInfo.UseShellExecute = false;
            cmdStartInfo.CreateNoWindow = true;

            Process cmdProcess = new Process();
            cmdProcess.StartInfo = cmdStartInfo;
            cmdProcess.ErrorDataReceived += cmd_Error;
            cmdProcess.OutputDataReceived += cmd_DataReceived;
            cmdProcess.Exited += CmdProcess_Exited;
            cmdProcess.EnableRaisingEvents = true;
            cmdProcess.Start();
            cmdProcess.BeginOutputReadLine();
            cmdProcess.BeginErrorReadLine();
            
        }

        private void CmdProcess_Exited(object sender, EventArgs e)
        {
            // TODO: Validate Patch
            _logBuilder.AppendLine("Patching finished");
            InstallNextPatch();
            //PatchingFinished?.Invoke();
        }
        private void cmd_DataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
                return;

            _logBuilder.AppendLine($"Butler Output: {e.Data}");

            if (e.Data.Contains("\"type\":\"progress\""))
            {
                const string progressToken = "\"progress\":";
                string percentStr = e.Data.Substring(e.Data.IndexOf(progressToken) + progressToken.Length);
                percentStr = percentStr.Substring(0, percentStr.IndexOf(','));
                double percent = double.Parse(percentStr) * 100;
                progressBar.Value = (int)percent;
                textBoxInformation.Text = "Patching...";
                if (textBoxInformation.Text != null)
                {
                    textBoxInformation.SelectionStart = textBoxInformation.Text.Length;
                    textBoxInformation.SelectionLength = 0;
                }
            }
        }

        private void cmd_Error(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
                _logBuilder.AppendLine($"{e.Data}");
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            _backgroundWorker?.CancelAsync();

            _logBuilder.AppendLine();
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZipExtractor.log"),
                _logBuilder.ToString());
        }
    }
}
