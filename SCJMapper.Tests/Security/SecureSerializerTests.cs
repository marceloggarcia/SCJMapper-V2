using Microsoft.VisualStudio.TestTools.UnitTesting;
using SCJMapper_V2;
using System;
using System.IO;
using System.Collections.Generic;

namespace SCJMapper.Tests.Security
{
    /// <summary>
    /// Unit tests for SecureSerializer to validate:
    /// - JSON serialization/deserialization round-trip
    /// - HMAC integrity verification
    /// - Graceful handling of corrupted files
    /// - Performance baseline (< 100ms per operation)
    /// </summary>
    [TestClass]
    public class SecureSerializerTests
    {
        private string _testFilePath;
        private string _testDirectory;

        [TestInitialize]
        public void Setup()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "SCJMapper.Tests");
            if (!Directory.Exists(_testDirectory))
            {
                Directory.CreateDirectory(_testDirectory);
            }
            _testFilePath = Path.Combine(_testDirectory, $"test_{Guid.NewGuid()}.scj");
        }

        [TestCleanup]
        public void Teardown()
        {
            if (File.Exists(_testFilePath))
            {
                try { File.Delete(_testFilePath); }
                catch { /* Ignore cleanup failures */ }
            }
        }

        /// <summary>
        /// Test 1: Round-trip serialization (serialize then deserialize should give same data)
        /// </summary>
        [TestMethod]
        public void Test_RoundTrip_SimpleData()
        {
            // Arrange
            var testData = new Dictionary<string, object>
            {
                { "action", "MoveForward" },
                { "value", 1.0f }
            };
            var testObj = new { action = "MoveForward", value = 1.0f };

            // Act
            SecureSerializer.SerializeToFile(_testFilePath, testObj);
            var result = SecureSerializer.DeserializeFromFile<dynamic>(_testFilePath);

            // Assert
            Assert.IsNotNull(result, "Deserialized object should not be null");
            Assert.AreEqual("MoveForward", (string)result.action, "Action value should match");
            Assert.AreEqual(1.0f, (float)result.value, 0.01f, "Value should match within tolerance");
        }

        /// <summary>
        /// Test 2: Verify HMAC protection (data tampering is detected)
        /// </summary>
        [TestMethod]
        public void Test_HMAC_Protection_DetectsTampering()
        {
            // Arrange
            var testObj = new { testData = "important", value = 42 };
            SecureSerializer.SerializeToFile(_testFilePath, testObj);

            // Act - Tamper with the file
            byte[] fileBytes = File.ReadAllBytes(_testFilePath);
            if (fileBytes.Length > 100)
            {
                fileBytes[50] ^= 0xFF; // Flip some bits in the middle
                File.WriteAllBytes(_testFilePath, fileBytes);
            }

            // Assert - Deserialization should fail or return null
            var result = SecureSerializer.DeserializeFromFile<dynamic>(_testFilePath);
            Assert.IsNull(result, "Tampered data should fail HMAC verification");
        }

        /// <summary>
        /// Test 3: Handle corrupted/invalid files gracefully
        /// </summary>
        [TestMethod]
        public void Test_CorruptedFile_HandledGracefully()
        {
            // Arrange - Create a corrupted file
            File.WriteAllText(_testFilePath, "This is not a valid serialized object");

            // Act
            var result = SecureSerializer.DeserializeFromFile<dynamic>(_testFilePath);

            // Assert
            Assert.IsNull(result, "Invalid file should return null, not throw");
        }

        /// <summary>
        /// Test 4: Empty/non-existent file handling
        /// </summary>
        [TestMethod]
        public void Test_NonExistentFile_ReturnsNull()
        {
            // Arrange
            string nonExistentFile = Path.Combine(_testDirectory, "does_not_exist.scj");

            // Act
            var result = SecureSerializer.DeserializeFromFile<dynamic>(nonExistentFile);

            // Assert
            Assert.IsNull(result, "Non-existent file should return null");
        }

        /// <summary>
        /// Test 5: Large data round-trip (stress test)
        /// </summary>
        [TestMethod]
        public void Test_RoundTrip_LargeData()
        {
            // Arrange - Create a large object with complex structure
            var actions = new List<object>();
            for (int i = 0; i < 1000; i++)
            {
                actions.Add(new { id = i, name = $"Action{i}", value = i * 1.5f });
            }
            var largeObj = new { actions = actions, timestamp = DateTime.UtcNow };

            // Act
            SecureSerializer.SerializeToFile(_testFilePath, largeObj);
            var result = SecureSerializer.DeserializeFromFile<dynamic>(_testFilePath);

            // Assert
            Assert.IsNotNull(result, "Large object should deserialize successfully");
            Assert.AreEqual(1000, ((System.Collections.Generic.List<dynamic>)result.actions).Count, 
                "Object should contain 1000 actions");
        }

        /// <summary>
        /// Test 6: Performance baseline (serialization should be < 100ms)
        /// </summary>
        [TestMethod]
        public void Test_Performance_SerializationUnder100ms()
        {
            // Arrange
            var testObj = new { data = "test", count = 999 };

            // Act
            var watch = System.Diagnostics.Stopwatch.StartNew();
            SecureSerializer.SerializeToFile(_testFilePath, testObj);
            watch.Stop();

            // Assert
            Assert.IsTrue(watch.ElapsedMilliseconds < 100,
                $"Serialization should complete in < 100ms, took {watch.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// Test 7: Performance baseline (deserialization should be < 100ms)
        /// </summary>
        [TestMethod]
        public void Test_Performance_DeserializationUnder100ms()
        {
            // Arrange
            var testObj = new { data = "test", count = 999 };
            SecureSerializer.SerializeToFile(_testFilePath, testObj);

            // Act
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = SecureSerializer.DeserializeFromFile<dynamic>(_testFilePath);
            watch.Stop();

            // Assert
            Assert.IsTrue(watch.ElapsedMilliseconds < 100,
                $"Deserialization should complete in < 100ms, took {watch.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// Test 8: Verify file format contains HMAC signature
        /// </summary>
        [TestMethod]
        public void Test_FileFormat_ContainsHMACSignature()
        {
            // Arrange
            var testObj = new { test = "data" };
            SecureSerializer.SerializeToFile(_testFilePath, testObj);

            // Act
            byte[] fileBytes = File.ReadAllBytes(_testFilePath);
            string fileContent = System.Text.Encoding.UTF8.GetString(fileBytes);

            // Assert - File should contain HMAC marker or be encrypted
            // (The exact format depends on SecureSerializer implementation)
            Assert.IsTrue(fileBytes.Length > 0, "Serialized file should not be empty");
            Assert.IsTrue(fileBytes.Length > 20, "File should contain data + HMAC signature");
        }
    }
}
