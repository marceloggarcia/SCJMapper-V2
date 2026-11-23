using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SCJMapper_V2.CryXMLlib;
using SCJMapper_V2.p4kFile;
using static SCJMapper_V2.SC.SCUiText;

namespace SCJMapper_V2.SC
{
  /// <summary>
  /// Manages all SC files from Pak
  /// tracks the filedate to update only if needed
  /// </summary>
  class SCFiles
  {
    private static readonly log4net.ILog log = log4net.LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod( ).DeclaringType );

    private const string LanguageCacheVersion = "localization-v2";

    private SCFile m_pakFile; // no content, carries only the filedate
    private SCFile m_defProfile;
    private Dictionary<string, SCFile> m_mapFiles;
    private Dictionary<string, SCFile> m_langFiles;

    // Singleton
    private static readonly Lazy<SCFiles> m_lazy = new Lazy<SCFiles>( () => new SCFiles( ) );
    public static SCFiles Instance { get => m_lazy.Value; }

    private SCFiles()
    {
      LoadPack( ); // get the lastest ones
    }


    public string DefaultProfile { get => m_defProfile.Filedata; }
    public IList<string> MapFiles
    {
      get => m_mapFiles.Keys.ToList( );
    }

    public string MapFile( string filekey )
    {
      if ( m_mapFiles.ContainsKey( filekey ) ) {
        return m_mapFiles[filekey].Filedata;
      }
      return "";
    }

    public IList<string> LangFiles
    {
      get => m_langFiles.Keys.ToList( );
    }
    public string LangFile( string filekey )
    {
      if ( m_langFiles.ContainsKey( filekey ) ) {
        return m_langFiles[filekey].Filedata;
      }
      return "";
    }

    /// <summary>
    /// Zip Extracts the file to a string
    /// SC Alpha 2.2: Have to find the new one in E:\G\StarCitizen\StarCitizen\LIVE\Data.p4k (contains the binary XML now)
    /// </summary>
    static private string ExtractDefaultBinProfileP4k( string defaultProfileName )
    {
      log.Debug( "ExtractDefaultBinProfileP4k - Entry" );

      string retVal = "";
      if ( File.Exists( SCPath.SCData_p4k ) ) {
        try {
          var PD = new p4kFile.p4kDirectory( );
          p4kFile.p4kFile p4K = PD.ScanDirectoryFor( SCPath.SCData_p4k, defaultProfileName );
          if ( p4K != null ) {
            byte[] fContent = PD.GetFile( SCPath.SCData_p4k, p4K );

            // use the binary XML reader
            CryXmlNodeRef ROOT = null;
            CryXmlBinReader.EResult readResult = CryXmlBinReader.EResult.Error;
            CryXmlBinReader cbr = new CryXmlBinReader( );
            ROOT = cbr.LoadFromBuffer( fContent, out readResult );
            if ( readResult == CryXmlBinReader.EResult.Success ) {
              XmlTree tree = new XmlTree( );
              tree.BuildXML( ROOT );
              retVal = tree.XML_string;
            }
            else {
              log.ErrorFormat( "  Error in CryXmlBinReader: {0}", cbr.GetErrorDescription( ) );
              retVal = ""; // clear any remanents
            }
          }
        }
        catch ( Exception ex ) {
          log.Error( "  Unexpected ", ex );
        }
      }
      return retVal;
    }

    /// <summary>
    /// Update from p4k (treats is like never read..)
    /// </summary>
    private void UpdatePakFile()
    {
      if ( File.Exists( SCPath.SCData_p4k ) ) {
        m_pakFile.Filetype = SCFile.FileType.PakFile;
        m_pakFile.Filename = Path.GetFileName( SCPath.SCData_p4k );
        m_pakFile.Filepath = Path.GetDirectoryName( SCPath.SCData_p4k );
        m_pakFile.FileDateTime = File.GetLastWriteTime( SCPath.SCData_p4k );
        m_pakFile.Filedata = "";
      }
    }

    /// <summary>
    /// Update from the pack file (treats it like never read..)
    /// </summary>
    private void UpdateDefProfileFile()
    {
      log.Info( "UpdateDefProfileFile - Entry" );

      string retVal = "";
      if ( File.Exists( SCPath.SCData_p4k ) ) {
        try {
          var PD = new p4kFile.p4kDirectory( );
          p4kFile.p4kFile p4K = PD.ScanDirectoryFor( SCPath.SCData_p4k, SCDefaultProfile.DefaultProfileName );
          if ( p4K != null ) {
            byte[] fContent = PD.GetFile( SCPath.SCData_p4k, p4K );
            // use the binary XML reader
            CryXmlNodeRef ROOT = null;
            CryXmlBinReader.EResult readResult = CryXmlBinReader.EResult.Error;
            CryXmlBinReader cbr = new CryXmlBinReader( );
            ROOT = cbr.LoadFromBuffer( fContent, out readResult );
            if ( readResult == CryXmlBinReader.EResult.Success ) {
              XmlTree tree = new XmlTree( );
              tree.BuildXML( ROOT );
              retVal = tree.XML_string;
              // make our file - only this one gets a new one
              m_defProfile.Filetype = SCFile.FileType.DefProfile;
              m_defProfile.Filename = Path.GetFileName( p4K.Filename );
              m_defProfile.Filepath = Path.GetDirectoryName( p4K.Filename );
              m_defProfile.FileDateTime = p4K.FileModifyDate;
              m_defProfile.Filedata = retVal;
              log.Info( "UpdateDefProfileFile - read from pak file" );
            }
            else {
              log.ErrorFormat( "UpdateDefProfileFile - Error in CryXmlBinReader: {0}", cbr.GetErrorDescription( ) );
              retVal = ""; // clear any remanents
            }
          }
        }
        catch ( Exception ex ) {
          log.Error( "UpdateDefProfileFile - Unexpected ", ex );
        }

      }
    }

    /// <summary>
    /// Update all map files from the pak file (treats it like never read..)
    /// </summary>
    private void UpdateMapFiles()
    {
      log.Info( "UpdateMapFiles - Entry" );

      if ( File.Exists( SCPath.SCData_p4k ) ) {
        try {
          var PD = new p4kDirectory( );
          IList<p4kFile.p4kFile> fileList = PD.ScanDirectoryContaining( SCPath.SCData_p4k, @"Data\Libs\Config\Mappings\layout_" );

          foreach ( p4kFile.p4kFile file in fileList ) {
            byte[] fContent = PD.GetFile( SCPath.SCData_p4k, file );

            // use the binary XML reader
            CryXmlNodeRef ROOT = null;
            CryXmlBinReader.EResult readResult = CryXmlBinReader.EResult.Error;
            CryXmlBinReader cbr = new CryXmlBinReader( );
            ROOT = cbr.LoadFromBuffer( fContent, out readResult );
            if ( readResult == CryXmlBinReader.EResult.Success ) {
              XmlTree tree = new XmlTree( );
              tree.BuildXML( ROOT );
              // make our file - only this one gets a new one
              SCFile obj = new SCFile {
                Filetype = SCFile.FileType.MapFile,
                Filename = Path.GetFileName( file.Filename ),
                Filepath = Path.GetDirectoryName( file.Filename ),
                FileDateTime = file.FileModifyDate,
                Filedata = tree.XML_string
              };
              // replace
              if ( m_mapFiles.ContainsKey( obj.Filename ) )
                m_mapFiles.Remove( obj.Filename );
              m_mapFiles.Add( obj.Filename, obj );
              log.InfoFormat( "UpdateMapFiles - read from pak file {0}", obj.Filename );
            }
            else {
              log.ErrorFormat( "UpdateMapFiles - Error in CryXmlBinReader: {0}", cbr.GetErrorDescription( ) );
            }
          }
        }
        catch ( Exception ex ) {
          log.Error( "UpdateMapFiles - Unexpected ", ex );
        }
      }
    }

    /// <summary>
    /// Update all language files from the pak file (treats it like never read..)
    /// </summary>
    private bool UpdateLangFiles()
    {
      log.Info( "UpdateLangFiles - Entry" );

      if ( File.Exists( SCPath.SCData_p4k ) ) {
        try {
          var PD = new p4kDirectory( );
          IList<p4kFile.p4kFile> fileList = PD.ScanDirectoryContaining( SCPath.SCData_p4k, @"\global.ini" );
          foreach ( p4kFile.p4kFile file in fileList ) {
            string lang = Path.GetFileNameWithoutExtension( Path.GetDirectoryName( file.Filename ) );
            if ( SCUiText.TryGetLanguage( lang, out Languages fileLang ) ) {
              byte[] fContent = PD.GetFile( SCPath.SCData_p4k, file );
              if ( fContent.Length == 0 ) {
                log.ErrorFormat( "UpdateLangFiles - could not read {0}", file.Filename );
                return false;
              }
              // make our file - only this one gets a new one
              SCFile obj = new SCFile {
                Filetype = SCFile.FileType.LangFile,
                Filename = fileLang.ToString( ),
                Filepath = Path.GetDirectoryName( file.Filename ),
                FileDateTime = file.FileModifyDate,
                Filedata = FilterLanguageContent( fContent )
              };
              // replace
              if ( m_langFiles.ContainsKey( obj.Filename ) )
                m_langFiles.Remove( obj.Filename );
              m_langFiles.Add( obj.Filename, obj );
              log.InfoFormat( "UpdateLangFiles - read from pak file {0}", obj.Filename );
            }

          }// all files
          return true;
        }
        catch ( Exception ex ) {
          log.Error( "UpdateLangFiles - Unexpected ", ex );
        }
      }
      return false;
    }

    private static string FilterLanguageContent( byte[] content )
    {
      var result = new StringBuilder( );
      using ( TextReader reader = new StreamReader( new MemoryStream( content ), Encoding.UTF8, true ) ) {
        string line;
        while ( ( line = reader.ReadLine( ) ) != null ) {
          if ( line.StartsWith( "ui_", StringComparison.Ordinal ) && line.IndexOf( '=' ) >= 0 ) {
            result.AppendLine( line );
          }
        }
      }
      return result.ToString( );
    }


    /// <summary>
    /// Load all SC assets from the local stored files
    /// avoiding to read through the p4k files at each startup
    /// </summary>
    private void LoadPack()
    {
      // make sure we have valid but empty ones
      m_pakFile = new SCFile( );
      m_defProfile = new SCFile( );
      m_mapFiles = new Dictionary<string, SCFile>( );
      m_langFiles = new Dictionary<string, SCFile>( );

      if ( !Directory.Exists( TheUser.FileStoreDir ) )
        return; // EXIT - no files to read from - first time maybe or deleted

      // Load serialized files with secure deserialization
      try {
        IEnumerable<string> filelist = Directory.EnumerateFiles( TheUser.FileStoreDir, "*.scj" );
        foreach ( string file in filelist ) {
          try {
            SCFile obj = SecureSerializer.DeserializeFromFile<SCFile>( file );
            if ( obj == null ) {
              log.WarnFormat( "LoadPack - Failed to deserialize {0}, skipping file", Path.GetFileName( file ) );
              continue;
            }

            if ( obj.Filetype == SCFile.FileType.PakFile ) {
              m_pakFile = obj;
            }
            else if ( obj.Filetype == SCFile.FileType.DefProfile ) {
              m_defProfile = obj;
            }
            else if ( obj.Filetype == SCFile.FileType.MapFile ) {
              m_mapFiles.Add( obj.Filename, obj );
            }
            else if ( obj.Filetype == SCFile.FileType.LangFile ) {
              m_langFiles.Add( obj.Filename, obj );
            }
          }
          catch ( Exception ex ) {
            log.WarnFormat( "LoadPack - Error deserializing {0}: {1}", Path.GetFileName( file ), ex.Message );
            continue; // Skip corrupted file and continue with others
          }
        }
      }
      catch ( Exception e ) {
        log.Error( "LoadPack - File enumeration error:", e );
        return; // ERROR EXIT - cannot read directory
      }


    }

    /// <summary>
    /// Save all assets to a local store
    /// </summary>
    private void SavePack()
    {
      if ( m_pakFile.Filetype != SCFile.FileType.PakFile ) {
        log.Error( "SavePack - no valid data to save?? " );
        return; // nothing to save ??
      }

      // make sure we have a folder to write to
      try {
        if ( !Directory.Exists( TheUser.FileStoreDir ) )
          Directory.CreateDirectory( TheUser.FileStoreDir );
      }
      catch ( Exception e ) {
        log.Error( "SavePack - create dir error:", e );
        return; // ERROR EXIT - cannot create a dir to write to
      }

      // Save all serialized objects with secure serialization
      try {
        // save p4k reference
        SecureSerializer.SerializeToFile( m_pakFile, Path.Combine( TheUser.FileStoreDir, m_pakFile.Filename + ".scj" ) );

        if ( m_defProfile != null && m_defProfile.Filetype == SCFile.FileType.DefProfile ) {
          SecureSerializer.SerializeToFile( m_defProfile, Path.Combine( TheUser.FileStoreDir, m_defProfile.Filename + ".scj" ) );
        }

        foreach ( KeyValuePair<string, SCFile> kv in m_mapFiles ) {
          if ( kv.Value != null && kv.Value.Filetype == SCFile.FileType.MapFile ) {
            SecureSerializer.SerializeToFile( kv.Value, Path.Combine( TheUser.FileStoreDir, kv.Value.Filename + ".scj" ) );
          }
        }

        foreach ( KeyValuePair<string, SCFile> kv in m_langFiles ) {
          if ( kv.Value != null && kv.Value.Filetype == SCFile.FileType.LangFile ) {
            SecureSerializer.SerializeToFile( kv.Value, Path.Combine( TheUser.FileStoreDir, kv.Value.Filename + ".scj" ) );
          }
        }
      }
      catch ( Exception e ) {
        log.Error( "SavePack - serialization error:", e );
        return; // ERROR EXIT - cannot write
      }
    }


    /// <summary>
    /// Checks if the p4k has changed since last read
    /// </summary>
    /// <returns>True if the p4k file is newer than the saved data</returns>
    private bool NeedsUpdate()
    {
      if ( string.IsNullOrEmpty( m_defProfile.Filedata ) ) return true;
      if ( m_pakFile.Filedata != LanguageCacheVersion ) return true;

      bool pakUpdated = true; // need to read
      // check pak and see if we had read it already
      if ( m_pakFile.Filetype == SCFile.FileType.PakFile ) {
        // seems we have read and saved some files at least once
        string pakPath = SCPath.SCData_p4k;
        if ( File.Exists( pakPath ) ) {
          if ( !string.Equals( m_pakFile.Filepath, Path.GetDirectoryName( pakPath ), StringComparison.OrdinalIgnoreCase )
            || !string.Equals( m_pakFile.Filename, Path.GetFileName( pakPath ), StringComparison.OrdinalIgnoreCase ) ) return true;
          DateTime dateTime = File.GetLastWriteTime( pakPath );
          pakUpdated = ( dateTime > m_pakFile.FileDateTime );
        }
      }
      return pakUpdated;
    }

    /// <summary>
    /// Load the asset files from the local store and reloads from p4k if needed only
    /// </summary>
    public void UpdatePack()
    {
      LoadPack( ); // get the lastest ones
                   // either we have files or not...
      if ( NeedsUpdate( ) == false ) return; // EXIT without update



      UpdatePakFile( );
      UpdateDefProfileFile( );
      UpdateMapFiles( );
      if ( UpdateLangFiles( ) ) m_pakFile.Filedata = LanguageCacheVersion;

      SavePack( ); // save the latest collection
    }




  }
}
