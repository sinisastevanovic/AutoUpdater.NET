using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace AutoUpdaterDotNET
{
    public class PatchInfo : IComparable<PatchInfo>
    {
        public string FileName { get; set; }
        public string Version { get; set; }

        public PatchInfo() { }

        public PatchInfo(string fileName, string version)
        {
            FileName = fileName;
            Version = version;
        }

        public int CompareTo(PatchInfo other)
        {
            if (other == null)
                return 1;

            if (IsVersionNewer(Version, other.Version))
                return 1;
            else
                return 0;
        }

        public static bool IsVersionNewer(string versionA, string versionB)
        {
            string stageA = "";
            int buildA = -1;
            Version versionNumberA = new Version();
            if (versionA.IndexOf('-') != -1)
            {
                string privateInfo = versionA.Substring(versionA.IndexOf('-') + 1);
                if (versionA.Contains("alpha"))
                    stageA = "alpha";
                else if (versionA.Contains("beta"))
                    stageA = "beta";

                if (privateInfo.IndexOf('.') != -1)
                {
                    buildA = int.Parse(privateInfo.Substring(privateInfo.IndexOf('.') + 1));
                }

                versionNumberA = new Version(versionA.Substring(0, versionA.IndexOf('-')));
            }
            else
            {
                versionNumberA = new Version(versionA);
            }

            string stageB = "";
            int buildB = -1;
            Version versionNumberB = new Version();
            if (versionB.IndexOf('-') != -1)
            {
                string privateInfo = versionB.Substring(versionB.IndexOf('-') + 1);
                if (versionB.Contains("alpha"))
                    stageB = "alpha";
                else if (versionB.Contains("beta"))
                    stageB = "beta";

                if (privateInfo.IndexOf('.') != -1)
                {
                    buildB = int.Parse(privateInfo.Substring(privateInfo.IndexOf('.') + 1));
                }

                versionNumberB = new Version(versionB.Substring(0, versionB.IndexOf('-')));
            }
            else
            {
                versionNumberB = new Version(versionB);
            }

            if (versionNumberA.Equals(versionNumberB))
            {
                if (stageA == stageB)
                {
                    return buildA > buildB;
                }
                else
                {
                    return (stageA == "" && stageB.Length > 0) || (stageA == "beta" && stageB == "alpha");
                }
            }
            else
            {
                return versionNumberA > versionNumberB;
            }
        }

        
    }
    /// <summary>
    ///     Object of this class gives you all the details about the update useful in handling the update logic yourself.
    /// </summary>
    [XmlRoot("item")]
    public class UpdateInfoEventArgs : EventArgs
    {
        private string _changelogURL;
        private string _downloadURL;

        /// <inheritdoc />
        public UpdateInfoEventArgs()
        {
            Mandatory = new Mandatory();
        }

        /// <summary>
        ///     If new update is available then returns true otherwise false.
        /// </summary>
        public bool IsUpdateAvailable { get; set; }
        
        /// <summary>
        ///     If there is an error while checking for update then this property won't be null.
        /// </summary>
        [XmlIgnore]
        public Exception Error { get; set; }

        /// <summary>
        ///     Download URL of the update file.
        /// </summary>
        [XmlElement("url")]
        public string DownloadURL
        {
            get => _downloadURL;
            set => _downloadURL = value;
        }

        /// <summary>
        ///     URL of the webpage specifying changes in the new update.
        /// </summary>
        [XmlElement("changelog")]
        public string ChangelogURL
        {
            get => _changelogURL;
            set => _changelogURL = value;
        }

        /// <summary>
        ///     Returns newest version of the application available to download.
        /// </summary>
        [XmlElement("version")]
        public string CurrentVersion { get; set; }

        /// <summary>
        ///     Returns version of the application currently installed on the user's PC.
        /// </summary>
        public Version InstalledVersion { get; set; }

        public string InstalledVersionFull { get; set; }

        public List<PatchInfo> PreviousUpdates { get; set; }

        /// <summary>
        ///     Shows if the update is required or optional.
        /// </summary>
        [XmlElement("mandatory")]
        public Mandatory Mandatory { get; set; }

        /// <summary>
        ///     Command line arguments used by Installer.
        /// </summary>
        [XmlElement("args")]
        public string InstallerArgs { get; set; }

        /// <summary>
        ///     Checksum of the update file.
        /// </summary>
        [XmlElement("checksum")]
        public CheckSum CheckSum { get; set; }

        internal static string GetURL(Uri baseUri, string url)
        {
            if (!string.IsNullOrEmpty(url) && Uri.IsWellFormedUriString(url, UriKind.Relative))
            {
                Uri uri = new Uri(baseUri, url);

                if (uri.IsAbsoluteUri)
                {
                    url = uri.AbsoluteUri;
                }
            }

            return url;
        }
    }

    /// <summary>
    ///     Mandatory class to fetch the XML values related to Mandatory field.
    /// </summary>
    public class Mandatory
    {
        /// <summary>
        ///     Value of the Mandatory field.
        /// </summary>
        [XmlText]
        public bool Value { get; set; }

        /// <summary>
        ///     If this is set and 'Value' property is set to true then it will trigger the mandatory update only when current installed version is less than value of this property.
        /// </summary>
        [XmlAttribute("minVersion")]
        public string MinimumVersion { get; set; }

        /// <summary>
        ///     Mode that should be used for this update.
        /// </summary>
        [XmlAttribute("mode")]
        public Mode UpdateMode { get; set; }
    }

    /// <summary>
    ///     Checksum class to fetch the XML values for checksum.
    /// </summary>
    public class CheckSum
    {
        /// <summary>
        ///     Hash of the file.
        /// </summary>
        [XmlText]
        public string Value { get; set; }

        /// <summary>
        ///     Hash algorithm that generated the hash.
        /// </summary>
        [XmlAttribute("algorithm")]
        public string HashingAlgorithm { get; set; }
    }
}