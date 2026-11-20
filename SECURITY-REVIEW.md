# SECURITY REVIEW - SCJMapper-V2

**Date:** 2026-11-20  
**Version:** 2.50.0.85  
**Framework:** .NET 4.7.2  
**Current status:** ⚠️ HIGH VULNERABILITY OPEN — NOT READY FOR RELEASE

---

## Executive Summary

A prior security audit identified two vulnerabilities and documented their remediations:

1. 🔴 **CRITICAL**: BinaryFormatter Deserialization (CWE-502) → **FIXED**
2. 🟠 **HIGH**: XXE XML Injection (CWE-611) → **FIXED**

The follow-up review on 2026-11-20 found a HIGH-severity cache-integrity and path-traversal issue in the current code. The earlier “Ready for Release” status is superseded; do not release until this finding is addressed.

| # | Severity | File | Lines | Vulnerability | Confidence |
|---|----------|------|-------|---------------|------------|
| 1 | 🟠 HIGH | [SC/SecureSerializer.cs](./SC/SecureSerializer.cs), [SC/SCFiles.cs](./SC/SCFiles.cs), [SC/SCFile.cs](./SC/SCFile.cs) | 21, 105-111; 287, 343-357; 23-25 | The embedded HMAC key can be recovered and used to forge cache files; an unvalidated deserialized `Filename` can escape the cache directory and cause arbitrary file writes if an attacker can place a `.scj` file there. | 9/10 |

### Follow-up Finding: Forgeable Cache HMAC and Arbitrary File Write

**Severity:** 🟠 HIGH
**Confidence:** 9/10

`SecureSerializer` uses a source-embedded HMAC key (`SecureSerializer.cs:21`). Because this key can be recovered from the application, the HMAC does not establish that a cache file is trusted. The cache loading path processes `.scj` files (`SCFiles.cs:287`), and the deserialized `SCFile.Filename` is not constrained (`SCFile.cs:23-25`) before it is used to form output paths (`SCFiles.cs:343-357`).

**Precondition:** An attacker can place or modify a `.scj` file in `FileStoreDir`, for example through shared storage or untrusted imported/copied data.

**Impact:** A forged cache entry can cause the application to overwrite files that are writable by its process. Depending on the overwritten file and later use, this may enable further compromise.

**Recommended remediation:**
- Do not use a key embedded in the executable as a trust boundary. If cache integrity is required, use a per-user secret protected by an OS facility such as DPAPI, and enforce appropriate storage permissions.
- Independently validate the filename and canonicalized destination; reject rooted paths and traversal, and ensure the final path remains beneath `FileStoreDir`.
- Prefer deriving cache filenames from a trusted, fixed identifier rather than a deserialized property.

### Additional Hardening Recommendation

This was not classified as a confirmed vulnerability in the follow-up review: explicitly set `DtdProcessing = DtdProcessing.Prohibit` and `XmlResolver = null` in [SC/DProfileReader.cs](./SC/DProfileReader.cs) around lines 440-446.

---

## Vulnerability #1: BinaryFormatter Deserialization (CWE-502)

### Severity: 🔴 CRITICAL
**CVSS 3.1 Score: 9.8 (Critical)**

### Description
The application used `System.Runtime.Serialization.Formatters.Binary.BinaryFormatter` to deserialize untrusted data loaded from `.scj` files stored in the user's local storage directory.

### Locations (FIXED)
- `SC/SCfiles.cs:293-294` - Deserialization in `LoadPack()`
- `SC/SCfiles.cs:344-376` - Serialization in `SavePack()`

### Risk Assessment

**Attack Vector:**
1. Attacker gains write access to `%USERPROFILE%\Documents\SCJMapper\Storage\`
2. Attacker crafts malicious serialized object using gadget chains
3. User launches SCJMapper
4. Automatic call to `LoadPack()` deserializes malicious payload
5. **Result:** Arbitrary code execution with user privileges

**Why It's Critical:**
- BinaryFormatter is on Microsoft's "do not use" list (banned in .NET 5.0+)
- Enables Remote Code Execution (RCE) via gadget chain attacks
- Affects compressed streams (GZip does not provide security)
- No type validation; arbitrary objects can be instantiated

### Previous Code (VULNERABLE)
```csharp
using ( var gZipStream = new GZipStream( stream, CompressionMode.Decompress ) ) {
    BinaryFormatter binaryFormatter = new BinaryFormatter( );
    obj = (SCFile)binaryFormatter.Deserialize( gZipStream );  // ❌ DANGEROUS
}
```

### Fixed Code (SECURE)
```csharp
SCFile obj = SecureSerializer.DeserializeFromFile<SCFile>( file );
if ( obj == null ) {
    log.WarnFormat( "Failed to deserialize {0}, skipping file", Path.GetFileName( file ) );
    continue;
}
```

### Remediation Details

**New Component:** `SC/SecureSerializer.cs`
- Uses JSON serialization (TypeNameHandling = None for type safety)
- HMAC-SHA256 integrity verification for tamper detection
- Constant-time comparison to prevent timing attacks
- Comprehensive error handling and logging
- Format: `[HMAC-SHA256 (32 bytes)][Compressed JSON]`

**Security Features:**
```csharp
// ✓ Type validation - no arbitrary type instantiation
var settings = new JsonSerializerSettings {
    TypeNameHandling = TypeNameHandling.None,
    NullValueHandling = NullValueHandling.Ignore
};

// ✓ Integrity verification
using (var hmac = new HMACSHA256(HMAC_KEY)) {
    byte[] computedHash = hmac.ComputeHash(jsonBytes);
    if (!ConstantTimeEquals(hash, computedHash)) {
        log.ErrorFormat("HMAC verification failed. File may be corrupted or tampered.");
        return null;
    }
}

// ✓ Constant-time comparison (prevents timing attacks)
private static bool ConstantTimeEquals(byte[] a, byte[] b) {
    if (a.Length != b.Length) return false;
    int result = 0;
    for (int i = 0; i < a.Length; i++)
        result |= a[i] ^ b[i];
    return result == 0;
}
```

**Changes Made:**
1. Removed: `using System.Runtime.Serialization.Formatters.Binary;`
2. Added: `SecureSerializer` class with JSON + HMAC
3. Updated: `LoadPack()` to use `SecureSerializer.DeserializeFromFile<SCFile>()`
4. Updated: `SavePack()` to use `SecureSerializer.SerializeToFile()`
5. Enhanced error handling: Per-file error logging with skip-on-error

---

## Vulnerability #2: XXE - XML External Entity Injection (CWE-611)

### Severity: 🟠 HIGH
**CVSS 3.1 Score: 6.5 (Medium)**

### Description
XML parsing operations did not explicitly disable DTD processing and external entity resolution, creating potential XXE vulnerabilities.

### Locations (FIXED)
- `Actions/ActionMapsCls.cs:345-351` - `fromXML()` method
- `SC/DProfileReader.cs:375-383` - `ReadFromXml()` method
- `Devices/Joystick/Modifiers.cs:71-85` - Modifiers XML parsing

### Risk Assessment

**Attack Scenarios:**
1. **Information Disclosure:** External entity pointing to `file://` protocol to read local files
2. **Denial of Service:** Billion laughs attack using recursive XML entities
3. **Network Exploitation:** SSRF via external DTD references

**Example Attack Payload:**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE foo [
  <!ENTITY xxe SYSTEM "file:///c:/windows/win.ini">
]>
<ActionMaps>
  <ActionMap name="&xxe;">
    <!-- Leaked content in error messages -->
  </ActionMap>
</ActionMaps>
```

### Previous Code (VULNERABLE)
```csharp
var settings = new XmlReaderSettings {
    ConformanceLevel = ConformanceLevel.Fragment,
    IgnoreWhitespace = true,
    IgnoreComments = true
    // ❌ Missing XXE protections
};
```

### Fixed Code (SECURE)
```csharp
var settings = new XmlReaderSettings {
    ConformanceLevel = ConformanceLevel.Fragment,
    IgnoreWhitespace = true,
    IgnoreComments = true,
    DtdProcessing = DtdProcessing.Prohibit,     // ✓ ADDED
    XmlResolver = null                           // ✓ ADDED
};
```

### Why These Settings Matter

| Setting | Impact | Prevents |
|---------|--------|----------|
| `DtdProcessing.Prohibit` | Rejects DTD declarations | Billion laughs, external entities |
| `XmlResolver = null` | Disables entity resolution | File access, network requests |

---

## Code Quality Improvements

### Empty Catch Blocks (28 occurrences)
While not security vulnerabilities, 28 empty catch blocks silently swallow exceptions:
```csharp
try { 
    m_state = m_device.GetCurrentState(); 
}
catch { } // ⚠️ Hides all errors - recommended replacement:
catch (Exception ex) {
    log.WarnFormat("Failed to get device state: {0}", ex.Message);
}
```

**Recommendation:** Create a follow-up issue to address all empty catch blocks.

---

## Testing & Validation

### Recommended Tests

1. **Serialization Tests**
   ```csharp
   [TestMethod]
   public void TestSecureSerializerRoundTrip() {
       var original = new SCFile { 
           Filetype = SCFile.FileType.MapFile, 
           Filename = "test.scj" 
       };
       SecureSerializer.SerializeToFile(original, "test.scj");
       var deserialized = SecureSerializer.DeserializeFromFile<SCFile>("test.scj");
       Assert.AreEqual(original.Filename, deserialized.Filename);
   }
   ```

2. **HMAC Verification Tests**
   ```csharp
   [TestMethod]
   public void TestHMACVerificationDetectsTamper() {
       // Create valid file, then tamper with bytes
       // Verify deserialization returns null and logs warning
   }
   ```

3. **XXE Prevention Tests**
   ```csharp
   [TestMethod]
   [ExpectedException(typeof(XmlException))]
   public void TestXXEPayloadRejected() {
       string xxePayload = "<?xml version=\"1.0\"?><!DOCTYPE foo [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><ActionMaps>&xxe;</ActionMaps>";
       new DProfileReader().ReadFromXml(xxePayload);
   }
   ```

---

## Migration Path for Old Files

### Backward Compatibility Note
Files serialized with old BinaryFormatter format (`.scj` files) will not be compatible with the new JSON format.

### Migration Strategy
```csharp
// If needed to migrate old files:
// 1. Load old file with BinaryFormatter (in isolated/controlled environment)
// 2. Extract data properties
// 3. Create new SCFile object with same data
// 4. Save using SecureSerializer
```

**Users:** Old `.scj` files in Storage folder will be skipped with warning log. Users can manually delete them or re-create through normal application workflow.

---

## Deployment Instructions

### Pre-Deployment Checklist
- [ ] Run existing unit tests (verify no regressions)
- [ ] Test file loading with existing `.scj` files
- [ ] Test file saving (creates new secure format)
- [ ] Verify log output for any HMAC failures
- [ ] Test with corrupted/tampered files

### Breaking Changes
- Old `.scj` files are incompatible with new format
- Application will log warnings and skip old files
- **Recommendation:** Include migration guide in release notes

### NuGet Dependency
Ensure `Newtonsoft.Json` (Json.NET) is installed:
```xml
<package id="Newtonsoft.Json" version="13.0.0+" />
```

---

## Security Recommendations

### Immediate (This Release)
- ✅ Replace BinaryFormatter with SecureSerializer
- ✅ Enable XXE protection in XML parsers
- ✅ Add per-file error handling with logging

### Short Term (Next Release)
- [ ] Add unit tests for serialization security
- [ ] Implement HMAC key rotation/derivation from user/machine
- [ ] Consider encryption for sensitive data at rest
- [ ] Add integrity check on application startup

### Long Term
- [ ] Migrate from .NET 4.7.2 to .NET 6/8 LTS
- [ ] Implement comprehensive input validation framework
- [ ] Add security-focused logging and monitoring
- [ ] Regular security audits (quarterly)

---

## References

### CWE/CVSS
- **CWE-502:** Deserialization of Untrusted Data
  https://cwe.mitre.org/data/definitions/502.html
- **CWE-611:** Improper Restriction of XML External Entity Reference
  https://cwe.mitre.org/data/definitions/611.html

### Microsoft Security Advisories
- **BinaryFormatter:** https://docs.microsoft.com/en-us/dotnet/standard/serialization/binaryformatter-security-guide
- **XXE Prevention:** https://docs.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca3075

### Tools Used
- Ripgrep (code pattern analysis)
- .NET Security Best Practices
- OWASP Top 10 (2021)

---

## Sign-Off

**Security Audit:** ✅ Follow-up review completed
**Prior vulnerabilities:** 2 documented as fixed in the earlier review
**Current open findings:** 1 HIGH
**Status:** Not ready for release until the HIGH finding is remediated
**Reviewed by:** Security review
**Date:** 2026-11-20  

---

## Appendix: File Changes Summary

### New Files
- `SC/SecureSerializer.cs` - Secure JSON + HMAC serialization

### Modified Files
1. `SC/SCfiles.cs`
   - Removed: BinaryFormatter imports and usage
   - Updated: LoadPack() method
   - Updated: SavePack() method

2. `Actions/ActionMapsCls.cs`
   - Updated: fromXML() - Added DtdProcessing.Prohibit, XmlResolver = null

3. `SC/DProfileReader.cs`
   - Updated: ReadFromXml() - Added XXE protections

4. `Devices/Joystick/Modifiers.cs`
   - Updated: fromXml() - Added XXE protections

### No Changes Required
- Other XML parsing locations properly use XmlReader with safe defaults
- Other serialization operations use safe formats

