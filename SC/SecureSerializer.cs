using System;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;

namespace SCJMapper_V2.SC
{
    /// <summary>
    /// Secure serialization helper - replaces unsafe BinaryFormatter
    /// Uses JSON with HMAC integrity verification
    /// </summary>
    static class SecureSerializer
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        // HMAC key - ideally should be derived from machine/user (not hardcoded for production)
        private static readonly byte[] HMAC_KEY = Encoding.UTF8.GetBytes("SCJMapper_V2_Security_Key_2024");

        /// <summary>
        /// Serialize object to compressed JSON with HMAC integrity check
        /// </summary>
        public static void SerializeToFile(object obj, string filePath)
        {
            try
            {
                // Serialize to JSON
                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.None,
                    NullValueHandling = NullValueHandling.Ignore
                };
                string json = JsonConvert.SerializeObject(obj, Formatting.None, settings);
                byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

                // Calculate HMAC for integrity verification
                using (var hmac = new HMACSHA256(HMAC_KEY))
                {
                    byte[] hash = hmac.ComputeHash(jsonBytes);

                    // Write: [HMAC (32 bytes)] + [JSON data]
                    using (var fileStream = File.Create(filePath))
                    {
                        using (var gzipStream = new GZipStream(fileStream, CompressionMode.Compress, leaveOpen: true))
                        {
                            // Write HMAC signature
                            gzipStream.Write(hash, 0, hash.Length);
                            // Write JSON data
                            gzipStream.Write(jsonBytes, 0, jsonBytes.Length);
                        }
                    }
                }

                log.DebugFormat("SecureSerializer.SerializeToFile - Successfully saved to {0}", filePath);
            }
            catch (Exception ex)
            {
                log.ErrorFormat("SecureSerializer.SerializeToFile - Error saving file {0}: {1}", filePath, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Deserialize object from compressed JSON with HMAC verification
        /// </summary>
        public static T DeserializeFromFile<T>(string filePath) where T : class
        {
            try
            {
                // Try new secure format first
                using (var fileStream = File.OpenRead(filePath))
                {
                    using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress, leaveOpen: true))
                    {
                        // Read and verify HMAC
                        byte[] hash = new byte[32];
                        int bytesRead = gzipStream.Read(hash, 0, 32);

                        if (bytesRead != 32)
                        {
                            log.WarnFormat("SecureSerializer.DeserializeFromFile - Invalid HMAC length in {0}", filePath);
                            return null;
                        }

                        // Read JSON data
                        using (var reader = new StreamReader(gzipStream, Encoding.UTF8))
                        {
                            string json = reader.ReadToEnd();
                            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

                            // Verify HMAC
                            using (var hmac = new HMACSHA256(HMAC_KEY))
                            {
                                byte[] computedHash = hmac.ComputeHash(jsonBytes);

                                if (!ConstantTimeEquals(hash, computedHash))
                                {
                                    log.ErrorFormat("SecureSerializer.DeserializeFromFile - HMAC verification failed for {0}. File may be corrupted or tampered.", filePath);
                                    return null;
                                }
                            }

                            // Deserialize with type validation
                            var settings = new JsonSerializerSettings
                            {
                                TypeNameHandling = TypeNameHandling.None,
                                NullValueHandling = NullValueHandling.Ignore
                            };

                            T result = JsonConvert.DeserializeObject<T>(json, settings);
                            log.DebugFormat("SecureSerializer.DeserializeFromFile - Successfully loaded from {0}", filePath);
                            return result;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.ErrorFormat("SecureSerializer.DeserializeFromFile - Error loading file {0}: {1}", filePath, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Constant-time comparison to prevent timing attacks
        /// </summary>
        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;

            int result = 0;
            for (int i = 0; i < a.Length; i++)
                result |= a[i] ^ b[i];

            return result == 0;
        }
    }
}
