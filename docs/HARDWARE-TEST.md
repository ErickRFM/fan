# Validación F1 en ASUS VivoBook Pro M3500QC
**No ejecutar pruebas a ciegas ni dejar el modo de prueba desatendido.**
1. Conectar cargador, despejar rejillas y cerrar otros controladores de ventilación.
2. Ejecutar el diagnóstico solo lectura y anotar modelo, versión BIOS, controlador ASUS y cantidad de ventiladores.
3. Abrir la app elevada y capturar RPM basales en AUTO; detenerse si son inválidas.
4. Pulsar TODOS 80% por 10 segundos y comprobar lectura y ruido.
5. Pulsar TODOS 100% por 10 segundos y comprobar cambios medidos de RPM.
6. Pulsar VOLVER A BIOS; comprobar que retorna a comportamiento automático.
7. Solo cuando lo anterior funcione, probar suspender, cerrar app y recuperación por watchdog.
8. Registrar datos/capturas anonimizados en issue de validación.

Detener inmediatamente ante RPM cero anómalas, bloqueos, temperaturas excesivas, olor a quemado o ruido mecánico. No escribir puertos EC arbitrarios, modificar firmware, sustituir drivers firmados, ni desactivar seguridad térmica.

| Verificación | Evidencia requerida | Estado |
|---|---|---|
| Modelo M3500QC y BIOS | Salida diagnóstico | Pendiente |
| FanCount y RPM automático | Registro real | Pendiente |
| PWM 80% | Variación real en RPM | Pendiente |
| PWM 100% | Variación real en RPM | Pendiente |
| Volver a BIOS | Lecturas y comportamiento | Pendiente |
| Recuperación watchdog | Resultado en PC físico | Pendiente |
