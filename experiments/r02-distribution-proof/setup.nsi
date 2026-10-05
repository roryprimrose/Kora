Unicode true
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WordFunc.nsh"
!insertmacro VersionCompare

Name "Kora R02 UNSIGNED distribution experiment"
OutFile "${SETUP}"
InstallDir "$PROGRAMFILES64\Kora-R02-Proof\${PROOF_ID}"
RequestExecutionLevel admin
SetCompressor /SOLID zlib
ShowInstDetails show
Page instfiles

!macro RequireRuntime FAMILY
  StrCpy $0 0
  loop_${FAMILY}:
    EnumRegValue $1 HKLM "SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\${FAMILY}" $0
    StrCmp $1 "" missing_${FAMILY}
    StrCpy $2 $1 5
    StrCmp $2 "10.0." 0 next_${FAMILY}
    ${VersionCompare} $1 "10.0.0" $2
    StrCmp $2 "2" next_${FAMILY} ready_${FAMILY}
  next_${FAMILY}:
    IntOp $0 $0 + 1
    Goto loop_${FAMILY}
  missing_${FAMILY}:
    MessageBox MB_OK|MB_ICONSTOP "Missing ${FAMILY} 10.0 x64. Install the supported .NET 10 x64 Desktop Runtime from https://dotnet.microsoft.com/download/dotnet/10.0 then retry. No SDK, source build, or automatic runtime install is provided."
    Abort
  ready_${FAMILY}:
!macroend

!macro RequireNative FILE
  IfFileExists "$SYSDIR\${FILE}" ready_${FILE}
    MessageBox MB_OK|MB_ICONSTOP "Missing x64 ${FILE}, imported by bundled ONNX Runtime. Install the supported Microsoft Visual C++ v14 x64 Redistributable from https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist then retry. No automatic installation is provided. File presence is not a functional loader test."
    Abort
  ready_${FILE}:
!macroend

Function .onInit
  IfSilent 0 +3
    MessageBox MB_OK|MB_ICONSTOP "Silent installation is disabled for this lab-only experiment."
    Abort
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "This proof requires Windows x64."
    Abort
  ${EndIf}
  SetRegView 64
  !insertmacro RequireRuntime Microsoft.NETCore.App
  !insertmacro RequireRuntime Microsoft.WindowsDesktop.App
  ${DisableX64FSRedirection}
  !insertmacro RequireNative VCRUNTIME140.dll
  !insertmacro RequireNative VCRUNTIME140_1.dll
  !insertmacro RequireNative MSVCP140.dll
  !insertmacro RequireNative MSVCP140_1.dll
  ${EnableX64FSRedirection}
  MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "LAB ONLY. UNSIGNED: Windows may show Unknown Publisher or SmartScreen. Hashes are traceability, not publisher authentication. This experiment copies a bootstrap into a separate Program Files folder; it does not launch Kora, register startup, install models, or replace an installation. Use only in an approved disposable lab. Future workers/resources and release acceptance remain unproven. Continue?" IDOK continue
  Abort
  continue:
  IfFileExists "$INSTDIR" 0 fresh
    MessageBox MB_OK|MB_ICONSTOP "Destination already exists. Nothing will be replaced. Use a fresh disposable lab."
    Abort
  fresh:
FunctionEnd

Section "Disposable protected payload"
  SetOutPath "$INSTDIR"
  File /r "${PAYLOAD}\*"
  File /oname=distribution-payload.json "${INSPECTION}"
  nsExec::ExecToStack '"$SYSDIR\icacls.exe" "$INSTDIR" /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-32-545:(OI)(CI)RX" /T'
  Pop $0
  Pop $1
  StrCmp $0 "0" 0 acl_failed
  nsExec::ExecToStack '"$SYSDIR\icacls.exe" "$INSTDIR" /setowner "*S-1-5-32-544" /T'
  Pop $0
  Pop $1
  StrCmp $0 "0" 0 acl_failed
  DetailPrint "Files copied; ACL requests completed. This is NOT proof of effective access, aliases, worker isolation, or safe launch. Do not enable execution capabilities."
  Goto done
  acl_failed:
    MessageBox MB_OK|MB_ICONSTOP "ACL setup failed. The partial lab payload is NOT a protected deployment. Do not launch it; reset the disposable lab."
    SetErrorLevel 1
    Abort
  done:
SectionEnd
