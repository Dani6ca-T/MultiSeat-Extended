MultiSeat Prerequisites
=======================

Run install-prerequisites.ps1 as Administrator.
All software is downloaded automatically — no manual steps required for most cases.

The script downloads and installs:

  ViGEmBus_1.22.0_x64_x86_arm64.exe   - Virtual Xbox controller driver (downloaded)
  HidHide_1.5.230_x64.exe             - Controller isolation driver (downloaded)
  VBCABLE_Driver_Pack45.zip            - VB-CABLE basic — 1 virtual audio device for seat 0 (downloaded)
  Voicemeeter8Setup_v3122.zip          - VoiceMeeter Potato — 3 virtual audio devices for seats 1-3 (downloaded)
  TermWrap-0.6.zip                     - Concurrent RDP sessions (in-memory Zydis patcher) (downloaded)
  Apollo-0.4.6.exe                     - Sunshine fork (multi-instance streaming) (downloaded)
  .NET 9 SDK                           - Installed via winget
  Node.js LTS                          - Installed via winget

Notes:
  - SudoVDA virtual display driver is bundled with Apollo — no separate install needed.
  - HidHide's silent install may not work on all machines. If it fails the script
    will launch the interactive installer automatically — just click through the wizard.
  - ViGEmBus installer may crash on certain machines (known Windows 11 issue). The
    script falls back to creating the device node via SetupAPI automatically.
  - Reboot when prompted — HidHide and TermWrap require it before the service will work.

To use offline/local files instead of downloading, place the installer files in
this folder before running the script — it will use them as-is.
