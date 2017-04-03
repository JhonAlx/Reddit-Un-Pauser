using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace Reddit_Un_Pauser.Model
{
    public class Configuration
    {
        [XmlIgnore] public const string FileName = "Settings.xml";

        public string Password;
        public string Username;

        [XmlIgnore]
        public static Configuration Instance { get; private set; }

        private void SetDefaultValues()
        {
            Username = string.Empty;
            Password = string.Empty;
        }

        public static void Default()
        {
            Instance = new Configuration();
            Instance.SetDefaultValues();
        }

        public static void Load()
        {
            var serializer = new XmlSerializer(typeof(Configuration));

            using (var fStream = new FileStream(FileName, FileMode.Open))
            {
                Instance = (Configuration) serializer.Deserialize(fStream);
            }

            Instance.Password = Instance.Password.DecryptString().ToInsecureString();
        }

        public void Save()
        {
            Password = Password.ToSecureString().EncryptString();

            var serializer = new XmlSerializer(typeof(Configuration));

            using (var fStream = new FileStream(FileName, FileMode.Create))
            {
                serializer.Serialize(fStream, this);
            }
        }
    }

    public static class SecureIt
    {
        private static readonly byte[] Entropy = Encoding.Unicode.GetBytes("Salt Is Not A Password");

        public static string EncryptString(this SecureString input)
        {
            if (input == null)
                return null;

            var encryptedData = ProtectedData.Protect(
                Encoding.Unicode.GetBytes(input.ToInsecureString()),
                Entropy,
                DataProtectionScope.CurrentUser);

            return Convert.ToBase64String(encryptedData);
        }

        public static SecureString DecryptString(this string encryptedData)
        {
            if (encryptedData == null)
                return null;

            try
            {
                var decryptedData = ProtectedData.Unprotect(
                    Convert.FromBase64String(encryptedData),
                    Entropy,
                    DataProtectionScope.CurrentUser);

                return Encoding.Unicode.GetString(decryptedData).ToSecureString();
            }
            catch
            {
                return new SecureString();
            }
        }

        public static SecureString ToSecureString(this IEnumerable<char> input)
        {
            if (input == null)
                return null;

            var secure = new SecureString();

            foreach (var c in input)
                secure.AppendChar(c);

            secure.MakeReadOnly();
            return secure;
        }

        public static string ToInsecureString(this SecureString input)
        {
            if (input == null)
                return null;

            var ptr = Marshal.SecureStringToBSTR(input);

            try
            {
                return Marshal.PtrToStringBSTR(ptr);
            }
            finally
            {
                Marshal.ZeroFreeBSTR(ptr);
            }
        }
    }
}