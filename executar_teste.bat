@echo off
echo ===============================================
echo     SCRIPT DE TESTE NAVSHIELDTRACER
echo ===============================================
echo.

echo 1. Compilando projetos...
dotnet build NavShieldTracer.sln -c Release
if %ERRORLEVEL% NEQ 0 (
    echo ERRO: Falha na compilacao!
    pause
    exit /b 1
)

echo.
echo 2. Projetos compilados com sucesso!
echo.
echo INSTRUCOES PARA TESTE:
echo.
echo A. Primeiro execute o NavShieldTracer como ADMINISTRADOR:
echo    dotnet run --project NavShieldTracer\NavShieldTracer.csproj -c Release
echo.
echo B. Quando solicitado, digite: teste
echo.
echo C. Em seguida, execute este comando em outro terminal:
echo    dotnet run --project TesteSoftware\TesteSoftware.csproj -c Release
echo.
echo D. O TesteSoftware aguardara 30 segundos antes de iniciar as atividades
echo    para dar tempo de configurar o monitoramento.
echo.

echo Pressione qualquer tecla para executar o TesteSoftware agora...
pause > nul

echo.
echo 3. Executando TesteSoftware...
dotnet run --project TesteSoftware\TesteSoftware.csproj -c Release

echo.
echo 4. Teste concluido!
pause