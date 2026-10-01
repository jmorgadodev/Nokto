@echo off
:: Script de Configuración de Red para Nokto LAN Remote (Ejecutar como Administrador)
echo ========================================================
echo   Configurando Permisos de Red y Firewall para Nokto
echo ========================================================
echo.

echo [1/2] Anadiendo regla en Windows Firewall (Puerto TCP 4884)...
netsh advfirewall firewall add rule name="Nokto LAN Remote" dir=in action=allow protocol=TCP localport=4884

echo.
echo [2/2] Registrando reserva URLACL para http.sys (http://+:4884/)...
netsh http add urlacl url=http://+:4884/ user=Everyone
if %errorlevel% neq 0 (
    netsh http add urlacl url=http://+:4884/ user=Todos
)

echo.
echo ========================================================
echo   Configuracion completada con exito.
echo ========================================================
pause
