# Inspeccion Selenium

Entrega de inspeccion funcional para `https://seleniumbase.io/demo_page`.

## Ejecucion

```powershell
python -m pip install -r requirements.txt
python inspeccion_demo_page.py
python generar_reporte.py
```

El script usa Selenium Manager para descargar o localizar el controlador compatible con Google Chrome. Genera capturas y `evidencias/resultados.json`. El generador crea `Reporte_Inspeccion_Selenium.pdf` con portada institucional y los resultados reales de la ejecucion.

`Reporte_Inspeccion_Selenium.tex` conserva una version LaTeX basada en la plantilla institucional proporcionada.

## Video de ejecucion

```powershell
python grabar_ejecucion.py
```

Genera `Ejecucion_Selenium.mp4` con Chrome visible mientras Selenium ejecuta los casos.
