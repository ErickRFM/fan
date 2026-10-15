## Entorno
- SO: Microsoft Windows 11 Home Single Language, 10.0.26300, x64
- Modelo ASUS: Vivobook_ASUSLaptop M3500QC_D3500QC
- BIOS: M3500QC.316 (24/05/2023)
- Procesador: AMD Ryzen 5 5600H (6C/12T)
- GPU: AMD Radeon + NVIDIA GeForce RTX 3050 Laptop
- .NET SDK: 8.0.426
- Git: 2.45.1.windows.1, gh: 2.96.0, winget: v1.29.380
- Servicios ASUS activos: AsusAppService, ASUSOptimization, AsusPTPService, ASUSSoftwareManager, ASUSSwitch, ASUSSystemAnalysis, ASUSSystemDiagnosis
- ASUS System Control Interface v3: 3.1.72.0

## Git
- Repo: https://github.com/ErickRFM/fan (PUBLIC)
- Rama: feat/windows-hardware-validation-v03
- Commits: 2 nuevos (e2c42d0, 7c0f0e9)
- PR #4: MERGED (https://github.com/ErickRFM/fan/pull/4)

## Compilación
- dotnet build FanForge.Win (Release): OK, 0 errores, 0 advertencias
- dotnet run FanForge.Tests: 44/44 OK
- scripts/compilar.ps1: genera dist\FanForge.exe (x64, single-file, self-contained)
- dist\FanForge.exe NO incluye AsusWinIO64.dll

## Pruebas
- Simuladas: 44/44 PASAN
- Físicas: BLOQUEADAS (driver AsusSAIO Stop Pending/Degraded)

## Hardware
- DLL OEM: asussci2.inf_amd64_197d319fab43e0fe (v1.1.4.0) — seleccionada por versión más reciente
- --probe elevado: FanCounts=-1, FanRPM=-1, Temp=0x7FFFFFFF
- PWM 80%/100%: no medible físicamente

## Hallazgos
- Regresión de driver ASUS (coincide con Karmel0x/AsusFanControl #44) bloquea HealthyTable API
- FanForge no modifica drivers/EC ni protecciones térmicas

## Pendientes/Acciones
- Reiniciar Windows; verificar AsusSAIO; verificar MyASUS Fan Diagnosis; repetir --probe; si OK, prueba supervisada 80%/100%.
