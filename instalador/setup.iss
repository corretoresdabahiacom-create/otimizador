; Instalador do Otimizador PC PRO (Inno Setup 6.1 ou superior)
; Compile com CriarInstalador.bat (na raiz do projeto) depois de rodar Publicar.bat.

#define AppName "Otimizador PC PRO"
#define AppVersion "3.2.0"
#define AppExe "OtimizadorPCPro.exe"

[Setup]
; Mesmo AppId da versão anterior: a instalação nova substitui a antiga.
AppId={{8E5F5E5B-8E8F-4F9E-8E8F-8E8F8E8F8E8F}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=OtimizadorPRO
AppSupportURL=mailto:admmeuarmazem@gmail.com
DefaultDirName={autopf}\Otimizador PC Pro
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\saida
OutputBaseFilename=OtimizadorPCPro_Setup_{#AppVersion}
SetupIconFile=..\src\Otimizador\Icone.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; Fecha o app aberto antes de atualizar
AppMutex=Local\OtimizadorPCPro_Instancia
CloseApplications=yes

[Languages]
Name: "portuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Área de Trabalho"; GroupDescription: "Atalhos:"

[InstallDelete]
; Restos da versão 2 (executável de 188 MB e script de "boot atrasado")
Type: files; Name: "{app}\Otimizador.exe"
Type: files; Name: "{app}\MENU_delay.vbs"
Type: files; Name: "{app}\Icone.ico"

[Files]
Source: "..\publicar\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir o {#AppName}"; Flags: postinstall nowait skipifsilent shellexec

[UninstallRun]
; Remove a tarefa "Iniciar com o Windows" criada pelo app
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""OtimizadorPCPro"" /F"; Flags: runhidden; RunOnceId: "RemoverTarefa"

[Code]
const
  RuntimeUrl = 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe';

var
  DownloadPage: TDownloadWizardPage;

{ Verifica se o .NET 8 Desktop Runtime (x64) está instalado }
function HasDesktopRuntime8: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 2) = '8.' then
        Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage('Baixando componentes', 'Baixando o .NET 8 Desktop Runtime da Microsoft…', nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if (CurPageID = wpReady) and (not HasDesktopRuntime8) then
  begin
    if MsgBox('O {#AppName} precisa do .NET 8 Desktop Runtime (gratuito, da Microsoft, ~55 MB).' + #13#10#13#10 +
              'Baixar e instalar agora?', mbConfirmation, MB_YESNO) = IDNO then
    begin
      MsgBox('Instale o .NET 8 Desktop Runtime (x64) em https://dotnet.microsoft.com/download/dotnet/8.0 e rode o instalador de novo.',
             mbInformation, MB_OK);
      Result := False;
      Exit;
    end;
    DownloadPage.Clear;
    DownloadPage.Add(RuntimeUrl, 'windowsdesktop-runtime-8-x64.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        if not Exec(ExpandConstant('{tmp}\windowsdesktop-runtime-8-x64.exe'), '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode)
           or ((ResultCode <> 0) and (ResultCode <> 3010) and (ResultCode <> 1638)) then
        begin
          MsgBox('Não foi possível instalar o .NET 8 Desktop Runtime (código ' + IntToStr(ResultCode) + ').', mbError, MB_OK);
          Result := False;
        end;
      except
        MsgBox('Falha no download: ' + GetExceptionMessage, mbError, MB_OK);
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;
