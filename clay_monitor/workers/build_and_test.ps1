# 构建验证脚本 - 需要 .NET 8.0 SDK
$ErrorActionPreference = "Stop"

Write-Host "=== 步骤 1: 检查 .NET SDK ===" -ForegroundColor Cyan
try {
    $dotnetVersion = & dotnet --version
    Write-Host ".NET SDK 版本: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "错误: 未安装 .NET SDK，请先安装 .NET 8.0 SDK" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== 步骤 2: 清理并还原 NuGet 包 ===" -ForegroundColor Cyan
& dotnet clean ClayMonitor.Workers.sln
& dotnet restore ClayMonitor.Workers.sln

Write-Host "`n=== 步骤 3: 构建所有项目 ===" -ForegroundColor Cyan
$buildResult = & dotnet build ClayMonitor.Workers.sln --configuration Debug --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "构建失败！" -ForegroundColor Red
    exit 1
}
Write-Host "构建成功！" -ForegroundColor Green

Write-Host "`n=== 步骤 4: 运行回归测试 ===" -ForegroundColor Cyan
$testResult = & dotnet test ClayMonitor.Workers.Tests/ClayMonitor.Workers.Tests.csproj --no-build --verbosity normal
if ($LASTEXITCODE -ne 0) {
    Write-Host "测试失败！" -ForegroundColor Red
    exit 1
}
Write-Host "`n=== 全部通过 ===" -ForegroundColor Green
Write-Host "所有项目构建成功，所有回归测试通过！" -ForegroundColor Green
exit 0
