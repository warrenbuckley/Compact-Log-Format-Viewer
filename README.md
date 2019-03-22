# Compact Log Format Viewer :mag: :chart_with_upwards_trend:
A cross platform tool to read &amp; query JSON aka CLEF log files created by Serilog

<img src="https://raw.githubusercontent.com/warrenbuckley/Compact-Log-Format-Viewer/master/LogViewer.Client/build/logo.png?v=2" width="100" height="100">

## Download
Releases are available on this GitHub Repository along on the Windows Store

### Windows
<a href='//www.microsoft.com/store/apps/9N8RV8LKTXRJ?cid=storebadge&ocid=badge'><img src='https://assets.windowsphone.com/85864462-9c82-451e-9355-a3d5f874397a/English_get-it-from-MS_InvariantCulture_Default.png' alt='English badge' style='width: 284px; height: 104px;' width="284" height="104" /></a>

## Build Status
[![Tests](https://dev.azure.com/warrenbuckley/Compact-Log-Viewer/_apis/build/status/warrenbuckley.Compact-Log-Format-Viewer?branchName=master&jobName=Server%20Project%20Tests&label=Tests)](https://dev.azure.com/warrenbuckley/Compact-Log-Viewer/_build/latest?definitionId=8&branchName=master)<br/>
[![Windows](https://dev.azure.com/warrenbuckley/Compact-Log-Viewer/_apis/build/status/warrenbuckley.Compact-Log-Format-Viewer?branchName=master&jobName=Windows%20Build&label=Windows)](https://dev.azure.com/warrenbuckley/Compact-Log-Viewer/_build/latest?definitionId=8&branchName=master)<br/>
[![MacOS](https://dev.azure.com/warrenbuckley/Compact-Log-Viewer/_apis/build/status/warrenbuckley.Compact-Log-Format-Viewer?branchName=master&jobName=OSX%20Build&label=MacOS)](https://dev.azure.com/warrenbuckley/Compact-Log-Viewer/_build/latest?definitionId=8&branchName=master)

Builds done by Azure Pipelines - https://dev.azure.com/warrenbuckley/Compact-Log-Viewer

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
- `dotnet publish --runtime osx-x64 --output bin/dist/osx /p:TrimUnusedDependencies=true` generates a self contained application for our WebAPI
- `dotnet publish --runtime win10-x64 --output bin/dist/win /p:TrimUnusedDependencies=true` same but creates the Windows version
- Change terminal directory to `LogViewer.Client` folder
- `npm install`
- `tsc --watch` This will compile the TypeScript files & continue to watch them
- Open a new terminal in `LogViewer.Client`
- `npm run start` Will run the Electron app for development with Chrome DevTools open/launched

>**Note:** If you `npm run start` before you have compiled the TypeScript files then Electron will complain about not finding the entry point file. Additionally if you have also not run `dotnet publish` then the underlying WebAPI which we communicate with will not be running.

## 3rd Party Libraries 💖💖
This package uses the following libraries:
- Serilog.Formatting.Compact.Reader
- Serilog.Filters.Expressions
- Serilog.Sinks.File
