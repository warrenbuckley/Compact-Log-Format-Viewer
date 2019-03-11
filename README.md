# Compact Log Format Viewer :mag: :chart_with_upwards_trend:
A cross platform tool to read &amp; query JSON aka CLEF log files created by Serilog

![Compact Log Format Viewer](https://raw.githubusercontent.com/warrenbuckley/Compact-Log-Format-Viewer/master/LogViewer.Client/build/logo.png)

## Build Status
Mac: [![Win & OSX Build Status](https://dev.azure.com/warrenbuckley/LogViewer/_apis/build/status/Windows%20&%20OSX%20Build?branchName=master)](https://dev.azure.com/warrenbuckley/LogViewer/_build/latest?definitionId=6&branchName=master)

Builds done by Azure Pipelines - https://dev.azure.com/warrenbuckley/LogViewer/

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
- `dotnet restore` Will fetch the dependencies needed for the WebAPI
- `dotnet build` Verify that there are no build errors
- `dotnet publish -r osx.10.11-x64 --output bin/dist/osx` generates a self contained application for our WebAPI
- `dotnet publish -r win10-x64 --output bin/dist/win` same but creates the Windows version
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
