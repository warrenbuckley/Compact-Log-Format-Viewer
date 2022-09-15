# Compact Log Format Viewer :mag: :chart_with_upwards_trend:
A cross platform tool to read &amp; query JSON aka CLEF log files created by Serilog

<img src="https://raw.githubusercontent.com/warrenbuckley/Compact-Log-Format-Viewer/master/LogViewer.Client/build/logo.png?v=2" width="100" height="100">

![screenshot](screenshot.JPG?raw=true "Screenshot")

## Download
Releases are available on this GitHub Repository along on the Windows Store

### Windows
<a href='https://www.microsoft.com/store/apps/9N8RV8LKTXRJ?cid=storebadge&ocid=badge'><img src='Get_it_from_Microsoft_Badge.png' alt='English badge' style='height: 38px;' height="38" /></a>

### MacOS
The metrics for the MacOS usage was too little & I don't currently build/release any other Apple apps, so my Apple Developer subscription lapsed. I assumed the application would still be available to download but that I would not be able to push any new updates. However it seems Apple just removes the listing :(

For now I recommend you build it manually. In future I may do auto-updates via GitHub releases instead of app stores.

## Building

You will need the following installed:
- node/npm
- .NET Core SDK 2.2+

For OSX & Windows you can download the SDK here or install Visual Studio for Mac/Windows which includes the `dotnet` CLI tool<br/>
https://dotnet.microsoft.com/download<br/>
https://visualstudio.microsoft.com/vs/

### Build Steps 🔨📐
- Clone Repo
- Open terminal in root of project
- `dotnet publish LogViewer.Server --runtime osx-x64 --output bin/dist/osx --configuration release -p:PublishSingleFile=true` generates a self contained application for our WebAPI
- `dotnet publish LogViewer.Server --runtime win-x64 --output bin/dist/win --configuration release -p:PublishSingleFile=true` same but creates the Windows version
- Change terminal directory to `LogViewer.Client` folder
- Install TypeScript if missing `npm install -g typescript`
- `npm install`
- `tsc --watch` This will compile the TypeScript files & continue to watch them
- Open a new terminal in `LogViewer.Client`
- `npm run start` Will run the Electron app for development with Chrome DevTools open/launched

>**Note:** If you `npm run start` before you have compiled the TypeScript files then Electron will complain about not finding the entry point file. Additionally if you have also not run `dotnet publish` then the underlying WebAPI which we communicate with will not be running.

## 3rd Party Libraries 💖💖
This package uses the following libraries:
- [Serilog.Formatting.Compact.Reader](https://github.com/serilog/serilog-formatting-compact-reader)
- [Serilog.Expressions](https://github.com/serilog/serilog-expressions)
- [Serilog.Sinks.File](https://github.com/serilog/serilog-sinks-file)
