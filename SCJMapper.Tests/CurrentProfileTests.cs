using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using SCJMapper_V2.p4kFile;

namespace SCJMapper.Tests
{
    [TestClass]
    public class CurrentProfileTests
    {
        private static readonly Assembly App = typeof(p4kDirectory).Assembly;

        [TestMethod]
        public void AssignsSolLeftAndRightDevicesToJs1AndJs2()
        {
            Type joystickType = App.GetType("SCJMapper_V2.Devices.Joystick.JoystickCls", true);
            MethodInfo sidePriority = joystickType.GetMethod("SolDeviceSidePriority");

            Assert.AreEqual(1, sidePriority.Invoke(null, new object[] { "SOL-R1 Left" }));
            Assert.AreEqual(1, sidePriority.Invoke(null, new object[] { "SOL R1 Izquierdo" }));
            Assert.AreEqual(2, sidePriority.Invoke(null, new object[] { "SOL-R2 Right" }));
            Assert.AreEqual(2, sidePriority.Invoke(null, new object[] { "SOL R2 Derecho" }));
            Assert.AreEqual(3, sidePriority.Invoke(null, new object[] { "SOL-R1" }));
        }

        [DataTestMethod]
        [DataRow((byte)0x04)]
        [DataRow((byte)0x14)]
        public void ReadsStandardAndCryLocalHeaders(byte signature)
        {
            string path = Path.GetTempFileName();
            try
            {
                using (var stream = File.Create(path))
                using (var writer = new BinaryWriter(stream))
                {
                    byte[] name = Encoding.ASCII.GetBytes("test.xml");
                    byte[] content = Encoding.ASCII.GetBytes("test");
                    writer.Write(new byte[] { 0x50, 0x4b, 0x03, signature });
                    writer.Write((ushort)20);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0x21);
                    writer.Write((uint)0);
                    writer.Write((uint)content.Length);
                    writer.Write((uint)content.Length);
                    writer.Write((ushort)name.Length);
                    writer.Write((ushort)0);
                    writer.Write(name);
                    writer.Write(content);
                }

                Type readerType = App.GetType("SCJMapper_V2.p4kFile.p4kRecReader", true);
                Type headerType = App.GetType("SCJMapper_V2.p4kFile.p4kFileHeader", true);
                using (var reader = (IDisposable)Activator.CreateInstance(readerType, path))
                using (var header = (IDisposable)Activator.CreateInstance(headerType, reader))
                {
                    Assert.AreEqual("test.xml", headerType.GetProperty("Filename").GetValue(header));
                    var data = (byte[])headerType.GetMethod("GetFile").Invoke(header, new object[] { reader });
                    Assert.AreEqual("test", Encoding.ASCII.GetString(data));
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ReadsNestedCurrentAndLegacyGamepadBindings()
        {
            Type mapsType = App.GetType("SCJMapper_V2.Actions.ActionMapsCls", true);
            FieldInfo maps = mapsType.GetField("ActionMaps");
            object original = maps.GetValue(null);
            try
            {
                maps.SetValue(null, new string[0]);
                Type readerType = App.GetType("SCJMapper_V2.SC.DProfileReader", true);
                object reader = Activator.CreateInstance(readerType);
                const string xml = "<profile><actionmap name='test'>"
                    + "<action name='current'><gamepad input='a'/></action>"
                    + "<action name='nested'><gamepad><inputdata input='b'/></gamepad></action>"
                    + "<action name='legacy'><xboxpad input='x'/></action>"
                    + "</actionmap></profile>";
                Assert.AreEqual(true, readerType.GetMethod("fromXML").Invoke(reader, new object[] { xml }));
                string csv = (string)readerType.GetProperty("CSVMap").GetValue(reader);
                StringAssert.Contains(csv, "Xcurrent;current;xi1_a;");
                StringAssert.Contains(csv, "Xnested;nested;xi1_b;");
                StringAssert.Contains(csv, "Xlegacy;legacy;xi1_x;");
            }
            finally
            {
                maps.SetValue(null, original);
            }
        }

        [TestMethod]
        public void AcceptsClientFolderContainingDataP4k()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(path);
            try
            {
                File.WriteAllBytes(Path.Combine(path, "Data.p4k"), new byte[0]);
                Type pathType = App.GetType("SCJMapper_V2.SC.SCPath", true);
                string issue = (string)pathType.GetMethod("CheckSCBasePath").Invoke(null, new object[] { path });
                Assert.AreEqual("", issue);
            }
            finally
            {
                File.Delete(Path.Combine(path, "Data.p4k"));
                Directory.Delete(path);
            }
        }

        [TestMethod]
        public void AcceptsExistingInstallationBaseFolder()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string game = Path.Combine(path, "StarCitizen");
            string live = Path.Combine(game, "LIVE");
            Directory.CreateDirectory(live);
            try
            {
                Type pathType = App.GetType("SCJMapper_V2.SC.SCPath", true);
                string issue = (string)pathType.GetMethod("CheckSCBasePath").Invoke(null, new object[] { path });
                Assert.AreEqual("", issue);
            }
            finally
            {
                Directory.Delete(live);
                Directory.Delete(game);
                Directory.Delete(path);
            }
        }
    }
}
