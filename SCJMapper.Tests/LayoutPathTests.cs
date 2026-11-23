using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Reflection;
using SCJMapper_V2.p4kFile;

namespace SCJMapper.Tests
{
    [TestClass]
    public class LayoutPathTests
    {
        [TestMethod]
        public void LayoutDirectoryIsRelativeToExecutableNotWorkingDirectory()
        {
            Type user = typeof(p4kDirectory).Assembly.GetType("SCJMapper_V2.TheUser", true);
            string appDir = (string)user.GetProperty("AppDir").GetValue(null);
            string original = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = Path.GetTempPath();
                string layouts = (string)user.GetProperty("LayoutsDir").GetValue(null);
                Assert.AreEqual(Path.Combine(appDir, "graphics", "layouts"), layouts);
            }
            finally
            {
                Environment.CurrentDirectory = original;
            }
        }
    }
}
