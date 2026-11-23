using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using SCJMapper_V2.p4kFile;

namespace SCJMapper.Tests
{
    [TestClass]
    public class LanguageTests
    {
        private static readonly Assembly App = typeof(p4kDirectory).Assembly;
        private static readonly Type UiText = App.GetType("SCJMapper_V2.SC.SCUiText", true);
        private static readonly Type Language = UiText.GetNestedType("Languages");
        private static readonly Type Tx = App.GetType("SCJMapper_V2.Translation.Tx", true);

        private static void SetInterfaceLanguage(string language)
        {
            Tx.GetMethod("SetLanguage").Invoke(null, new[] { Enum.Parse(Language, language) });
        }

        private static string InterfaceText(string key)
        {
            return (string)Tx.GetMethod("Translate", new[] { typeof(string) }).Invoke(null, new object[] { key });
        }

        [TestMethod]
        public void InterfaceLanguageUsesSelectedCultureAndProfileRestoresDefault()
        {
            FieldInfo usedCulture = Tx.GetField("UsedCulture");
            object original = usedCulture.GetValue(null);
            try
            {
                string[,] cases = {
                    { "spanish_latin_america", "es", "Aceptar" },
                    { "spanish_spain", "es", "Aceptar" },
                    { "english", "en", "Accept" },
                    { "french", "fr", "Accepter" },
                    { "german", "de", "Akzeptieren" }
                };
                for (int i = 0; i < cases.GetLength(0); i++)
                {
                    SetInterfaceLanguage(cases[i, 0]);
                    Assert.AreEqual(cases[i, 1], ((CultureInfo)usedCulture.GetValue(null)).Name);
                    Assert.AreEqual(cases[i, 2], InterfaceText("btAccept"));
                }
                SetInterfaceLanguage("profile");
                Assert.AreEqual(Tx.GetField("DefaultCulture", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), usedCulture.GetValue(null));
            }
            finally
            {
                usedCulture.SetValue(null, original);
            }
        }

        [TestMethod]
        public void LocalizesNestedMenusAndCanSwitchBackToEnglish()
        {
            FieldInfo usedCulture = Tx.GetField("UsedCulture");
            object original = usedCulture.GetValue(null);
            Assembly winForms = App.GetType("SCJMapper_V2.FormSettings", true).BaseType.Assembly;
            Type menuType = winForms.GetType("System.Windows.Forms.MenuStrip", true);
            Type itemType = winForms.GetType("System.Windows.Forms.ToolStripItem", true);
            Type menuItemType = winForms.GetType("System.Windows.Forms.ToolStripMenuItem", true);
            object menu = Activator.CreateInstance(menuType);
            object parent = Activator.CreateInstance(menuItemType);
            object child = Activator.CreateInstance(menuItemType);
            menuItemType.GetProperty("Name").SetValue(child, "meSettingsDialog");
            menuItemType.GetProperty("Tag").SetValue(child, "§");
            object children = menuItemType.GetProperty("DropDownItems").GetValue(parent);
            children.GetType().GetMethod("Add", new[] { itemType }).Invoke(children, new[] { child });
            object items = menuType.GetProperty("Items").GetValue(menu);
            items.GetType().GetMethod("Add", new[] { itemType }).Invoke(items, new[] { parent });
            try
            {
                SetInterfaceLanguage("spanish_spain");
                Tx.GetMethod("LocalizeControlTree").Invoke(null, new[] { menu });
                Assert.AreEqual("Configuración...", menuItemType.GetProperty("Text").GetValue(child));
                SetInterfaceLanguage("english");
                Tx.GetMethod("LocalizeControlTree").Invoke(null, new[] { menu });
                Assert.AreEqual("Settings Dialog...", menuItemType.GetProperty("Text").GetValue(child));
            }
            finally
            {
                ((IDisposable)menu).Dispose();
                usedCulture.SetValue(null, original);
            }
        }

        [TestMethod]
        public void LocalizesReusedSettingsAndLayoutDialogs()
        {
            FieldInfo usedCulture = Tx.GetField("UsedCulture");
            object original = usedCulture.GetValue(null);
            Type settingsType = App.GetType("SCJMapper_V2.FormSettings", true);
            Type layoutType = App.GetType("SCJMapper_V2.Layout.FormLayout", true);
            object settings = Activator.CreateInstance(settingsType);
            object layout = Activator.CreateInstance(layoutType);
            try
            {
                SetInterfaceLanguage("spanish_latin_america");
                settingsType.GetMethod("ApplyLocalization", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(settings, null);
                Tx.GetMethod("LocalizeControlTree").Invoke(null, new[] { layout });
                Assert.AreEqual("Configuración", settingsType.GetProperty("Text").GetValue(settings));
                Assert.AreEqual("Diseño del dispositivo", layoutType.GetProperty("Text").GetValue(layout));
                SetInterfaceLanguage("english");
                settingsType.GetMethod("ApplyLocalization", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(settings, null);
                Assert.AreEqual("Settings", settingsType.GetProperty("Text").GetValue(settings));
            }
            finally
            {
                ((IDisposable)settings).Dispose();
                ((IDisposable)layout).Dispose();
                usedCulture.SetValue(null, original);
            }
        }

        [TestMethod]
        public void LanguageChangesPreserveDeviceNames()
        {
            FieldInfo usedCulture = Tx.GetField("UsedCulture");
            object original = usedCulture.GetValue(null);
            try
            {
                foreach (string typeName in new[] {
                    "SCJMapper_V2.Devices.Joystick.UC_JoyPanel",
                    "SCJMapper_V2.Devices.Gamepad.UC_GpadPanel"
                })
                {
                    Type type = App.GetType(typeName, true);
                    object panel = Activator.CreateInstance(type);
                    try
                    {
                        type.GetProperty("Caption").SetValue(panel, "Test device");
                        SetInterfaceLanguage("spanish_spain");
                        Tx.GetMethod("LocalizeControlTree").Invoke(null, new[] { panel });
                        object caption = type.GetField("gbxBoxCap", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                        Assert.AreEqual("Test device", caption.GetType().GetProperty("Text").GetValue(caption));
                    }
                    finally
                    {
                        ((IDisposable)panel).Dispose();
                    }
                }
            }
            finally
            {
                usedCulture.SetValue(null, original);
            }
        }

        [TestMethod]
        public void ReusedTableUpdatesItsButtonsWhenLanguageChanges()
        {
            FieldInfo usedCulture = Tx.GetField("UsedCulture");
            object original = usedCulture.GetValue(null);
            Type tableType = App.GetType("SCJMapper_V2.Table.FormTable", true);
            object table = Activator.CreateInstance(tableType);
            try
            {
                MethodInfo localize = tableType.GetMethod("ApplyLocalization", BindingFlags.Instance | BindingFlags.NonPublic);
                object button = tableType.GetField("btClrFilterAction", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(table);
                SetInterfaceLanguage("spanish_spain");
                localize.Invoke(table, null);
                Assert.AreEqual("Limpiar", button.GetType().GetProperty("Text").GetValue(button));
                SetInterfaceLanguage("english");
                localize.Invoke(table, null);
                Assert.AreEqual("Clear", button.GetType().GetProperty("Text").GetValue(button));
            }
            finally
            {
                ((IDisposable)table).Dispose();
                usedCulture.SetValue(null, original);
            }
        }

        [DataTestMethod]
        [DataRow("spanish_(latin_america)", "spanish_latin_america")]
        [DataRow("spanish_(spain)", "spanish_spain")]
        [DataRow("french_(france)", "french")]
        [DataRow("german_(germany)", "german")]
        [DataRow("english", "english")]
        public void RecognizesGameLocalizationFolders(string folder, string expected)
        {
            object[] args = { folder, null };
            Assert.AreEqual(true, UiText.GetMethod("TryGetLanguage").Invoke(null, args));
            Assert.AreEqual(expected, args[1].ToString());
        }

        [TestMethod]
        public void LoadsSpanishActionLabelsFromCachedGameData()
        {
            Type filesType = App.GetType("SCJMapper_V2.SC.SCFiles", true);
            object files = filesType.GetProperty("Instance").GetValue(null);
            FieldInfo languageFiles = filesType.GetField("m_langFiles", BindingFlags.Instance | BindingFlags.NonPublic);
            object original = languageFiles.GetValue(files);
            try
            {
                var testFiles = (IDictionary)Activator.CreateInstance(languageFiles.FieldType);
                Type fileType = App.GetType("SCJMapper_V2.SC.SCFile", true);
                object file = Activator.CreateInstance(fileType);
                fileType.GetProperty("Filedata").SetValue(file, "ui_CTest=Acción\nui_v_test=Aceleración\n");
                testFiles.Add("spanish_latin_america", file);
                languageFiles.SetValue(files, testFiles);

                object ui = Activator.CreateInstance(UiText);
                UiText.GetProperty("Language").SetValue(ui, Enum.Parse(Language, "spanish_latin_america"));
                MethodInfo text = UiText.GetMethod("Text", new[] { typeof(string), typeof(string) });
                Assert.AreEqual("Acción", text.Invoke(ui, new object[] { "@ui_CTest", "fallback" }));
                Assert.AreEqual("Aceleración", text.Invoke(ui, new object[] { "@ui_v_test", "fallback" }));
                Assert.AreEqual("fallback", text.Invoke(ui, new object[] { "@missing", "fallback" }));
            }
            finally
            {
                languageFiles.SetValue(files, original);
            }
        }

        [TestMethod]
        public void KeepsModernLabelsAndUtf8TextWhenFilteringLanguageData()
        {
            Type filesType = App.GetType("SCJMapper_V2.SC.SCFiles", true);
            MethodInfo filter = filesType.GetMethod("FilterLanguageContent", BindingFlags.Static | BindingFlags.NonPublic);
            byte[] data = Encoding.UTF8.GetPreamble();
            byte[] text = Encoding.UTF8.GetBytes("ui_v_test=Aceleración\nui_CTest=Acción\nother=ignored\n");
            Array.Resize(ref data, data.Length + text.Length);
            Array.Copy(text, 0, data, data.Length - text.Length, text.Length);
            string result = (string)filter.Invoke(null, new object[] { data });
            StringAssert.Contains(result, "ui_v_test=Aceleración");
            StringAssert.Contains(result, "ui_CTest=Acción");
            Assert.IsFalse(result.Contains("other=ignored"));
        }
    }
}
