@echo off
set /p name= "Enter version tag: "

dotnet publish TwitchUnjail.Cli\TwitchUnjail.Cli.csproj --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:PublishTrimmed=true --framework net6.0 --runtime win-x64 --configuration Release --output publish\win-x64
dotnet publish TwitchUnjail.Cli\TwitchUnjail.Cli.csproj --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:PublishTrimmed=true --framework net6.0 --runtime linux-x64 --configuration Release --output publish\linux-x64
dotnet publish TwitchUnjail.Cli\TwitchUnjail.Cli.csproj --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:PublishTrimmed=true --framework net6.0 --runtime osx-x64 --configuration Release --output publish\osx-x64

copy publish\win-x64\TwitchUnjail.Cli.exe publish\twitch-unjail-%name%-win64.exe
copy publish\linux-x64\TwitchUnjail.Cli publish\twitch-unjail-%name%-linux64
copy publish\osx-x64\TwitchUnjail.Cli publish\twitch-unjail-%name%-osx64

pause
