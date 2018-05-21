set PATH=%localappdata%\Microsoft\dotnet;%PATH%
set Root=%~dp0

msbuild "%~dp0\sign.proj" /t:SignPackages %*