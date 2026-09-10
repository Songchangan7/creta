param(
    [string]$config = "Release",
    [string]$solution = (Join-Path $PSScriptRoot ".." -Resolve)
)
Write-Host "Config: $config"

function Build-Version {
    if (![string]::IsNullOrEmpty($env:CretaVersion)) {
        $v = $env:CretaVersion
    } elseif (![string]::IsNullOrEmpty($env:flowVersion)) {
        $v = $env:flowVersion
    } else {
        $targetPath = Join-Path $solution "Output/Release/Creta.dll" -Resolve
        $v = (Get-Command ${targetPath}).FileVersionInfo.FileVersion
    }

    Write-Host "Build Version: $v"
    return $v
}

function Build-Path {
    if (![string]::IsNullOrEmpty($env:APPVEYOR_BUILD_FOLDER)) {
        $p = $env:APPVEYOR_BUILD_FOLDER
    } elseif (![string]::IsNullOrEmpty($solution)) {
        $p = $solution
    } else {
        $p = Get-Location
    }

    Write-Host "Build Folder: $p"
    Set-Location $p

    return $p
}

function Copy-Resources ($path) {
    # making version static as multiple versions can exist in the nuget folder and in the case a breaking change is introduced.
    Copy-Item -Force $env:USERPROFILE\.nuget\packages\squirrel.windows\1.9.0\tools\Squirrel.exe $path\Output\Update.exe
}

function Delete-Unused ($path, $config) {
    $target = "$path\Output\$config"
    $included = Get-ChildItem $target -Filter "*.dll"
    foreach ($i in $included){
        $deleteList = Get-ChildItem $target\Plugins -Include $i -Recurse | Where { $_.VersionInfo.FileVersion -eq $i.VersionInfo.FileVersion -And $_.Name -eq "$i" }
        $deleteList | ForEach-Object{ Write-Host Deleting duplicated $_.Name with version $_.VersionInfo.FileVersion at location $_.Directory.FullName }
        $deleteList | Remove-Item
    }
    Remove-Item -Path $target -Include "*.xml" -Recurse
}

function Remove-CreateDumpExe ($path, $config) {
    $target = "$path\Output\$config"

    $depjson = Get-Content $target\Creta.deps.json -raw
    $depjson -replace '(?s)(.createdump.exe": {.*?}.*?\n)\s*', "" | Out-File $target\Creta.deps.json -Encoding UTF8
    Remove-Item -Path $target -Include "*createdump.exe" -Recurse
}


function Validate-Directory ($output) {
    New-Item $output -ItemType Directory -Force
}


function Pack-Squirrel-Installer ($path, $version, $output) {
    # msbuild based installer generation is not working in appveyor, not sure why
    Write-Host "Begin pack squirrel installer"

    $spec = "$path\Scripts\creta.nuspec"
    $input = "$path\Output\Release"

    Write-Host "Packing: $spec"
    Write-Host "Input path:  $input"

    # dotnet pack is not used because ran into issues, need to test installation and starting up if to use it.
    nuget pack $spec -Version $version -BasePath $input -OutputDirectory $output -Properties Configuration=Release

    $nupkg = "$output\Creta.$version.nupkg"
    Write-Host "nupkg path: $nupkg"
    $icon = "$path\Creta\Resources\app.ico"
    Write-Host "icon: $icon"
    # Squirrel.com: https://github.com/Squirrel/Squirrel.Windows/issues/369
    New-Alias Squirrel $env:USERPROFILE\.nuget\packages\squirrel.windows\1.9.0\tools\Squirrel.exe -Force
    # why we need Write-Output: https://github.com/Squirrel/Squirrel.Windows/issues/489#issuecomment-156039327
    # directory of releaseDir in squirrel can't be same as directory ($nupkg) in releasify
    $temp = "$output\Temp"

    Squirrel --releasify $nupkg --releaseDir $temp --setupIcon $icon --no-msi | Write-Output
    Move-Item $temp\* $output -Force
    Remove-Item $temp

    $file = "$output\Creta-Setup.exe"
    Write-Host "Filename: $file"

    Move-Item "$output\Setup.exe" $file -Force

    Write-Host "End pack squirrel installer"
}

function Publish-Self-Contained ($p) {

    $csproj  = Join-Path "$p" "Creta/Creta.csproj" -Resolve
    $profile = Join-Path "$p" "Creta/Properties/PublishProfiles/Net9.0-SelfContained.pubxml" -Resolve

    # we call dotnet publish on the main project. 
    # The other projects should have been built in Release at this point.
    dotnet publish -c Release $csproj /p:PublishProfile=$profile
}

function Publish-Portable ($outputLocation, $version) {

    & $outputLocation\Creta-Setup.exe --silent | Out-Null
    mkdir "$env:LocalAppData\Creta\app-$version\UserData"
    Compress-Archive -Path $env:LocalAppData\Creta -DestinationPath $outputLocation\Creta-Portable.zip
}

function Main {
    $p = Build-Path
    $v = Build-Version
    Copy-Resources $p

    if ($config -eq "Release"){

        Delete-Unused $p $config

        Publish-Self-Contained $p

        Remove-CreateDumpExe $p $config

        $o = "$p\Output\Packages"
        Validate-Directory $o
        Pack-Squirrel-Installer $p $v $o

        Publish-Portable $o $v
    }
}

Main