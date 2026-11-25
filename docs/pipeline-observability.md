# Registro de ejecuciones CI/CD en OpenObserve

> **Estado:** integración preparada y probada únicamente con HTTP local simulado. No se ha ejecutado GitHub Actions ni enviado información a una instancia real de OpenObserve.

## Arquitectura y flujo

El workflow existente conserva sus tres etapas y dependencias:

```text
test (Jest + .NET/coverage) → publish (Docker Hub, sólo push/main) → deploy (EC2 blue/green)
                                                                    ↓
                      pipeline-observability (always, sólo HTTP JSON) → OpenObserve
```

`pipeline-observability` espera el estado terminal de `test`, `publish` y `deploy` mediante `needs`, pero usa `if: ${{ always() }}`. Por tanto se planifica aun cuando una etapa previa falle, se cancele o se omita. No ejecuta Docker, SSH, AWS ni el script de despliegue.

El job tiene `contents: read` para leer el script y `actions: read` para consultar el recurso de ejecución de GitHub Actions. No recibe permisos de escritura. El paso de envío usa `continue-on-error: true` y el script devuelve de forma segura ante cualquier fallo de telemetría: OpenObserve no puede cambiar el resultado de pruebas, publicación ni despliegue.

## Configuración pendiente en GitHub

Configure los valores externos antes de esperar ingesta real:

| Tipo | Nombre | Uso |
|---|---|---|
| Variable de repositorio/organización | `OPENOBSERVE_URL` | **URL HTTPS exacta de ingesta JSON** entregada por la instancia, incluido el path. No se construye a partir de supuestos. |
| Variable de repositorio/organización | `OPENOBSERVE_ORGANIZATION` | Organización confirmada por OpenObserve. |
| Variable de repositorio/organización | `OPENOBSERVE_PIPELINE_STREAM` | Stream JSON de CI/CD confirmado por OpenObserve. |
| Secret de repositorio/organización | `OPENOBSERVE_USERNAME` | Usuario o identificador de ingesta entregado por el administrador. |
| Secret de repositorio/organización | `OPENOBSERVE_PASSWORD` | Contraseña o token compatible con autenticación Basic, entregado como secreto. |

`OPENOBSERVE_URL` debe ser la URL exacta; por ejemplo, la documentación de OpenObserve describe formatos JSON que terminan en `/_json`, pero no se asume que ése sea el endpoint, organización o stream de esta instalación. Se exige además organización y stream para evitar enviar cuando la configuración está incompleta, aunque la URL sea quien determina el destino HTTP.

No agregue estos valores a archivos versionados. GitHub enmascara secrets conocidos, pero el script además nunca escribe valores de configuración, headers ni errores de respuesta en los logs.

## Evento JSON

El script `scripts/pipeline-observability.js` envía un arreglo JSON con un evento. Ejemplo ilustrativo sin credenciales:

```json
[
  {
    "event_type": "ci_cd_pipeline",
    "workflow_name": "CI/CD",
    "workflow_run_id": 123,
    "workflow_run_number": 45,
    "job_name": "pipeline-observability",
    "status": "success",
    "conclusion": "success",
    "repository": "owner/webapp",
    "branch": "main",
    "commit_sha": "<commit-sha>",
    "actor": "<github-actor>",
    "trigger_event": "push",
    "run_url": "https://github.com/owner/webapp/actions/runs/123",
    "started_at": "2026-11-25T12:00:00.000Z",
    "completed_at": "2026-11-25T12:05:00.000Z",
    "duration_seconds": 300,
    "deployment_status": "success",
    "environment": "production",
    "test_status": "success",
    "publish_status": "success"
  }
]
```

`status` y `conclusion` se calculan desde los resultados de las etapas: `failure` tiene prioridad, seguido de `cancelled`, `success` y `skipped`. `deployment_status`, `test_status` y `publish_status` permiten identificar la fase afectada sin emitir un evento por job. `environment` sólo se informa si el job de despliegue no fue omitido. En un pull request, `publish` y `deploy` normalmente serán `skipped`; una prueba correcta conserva el resultado global `success`.

El script consulta el endpoint de ejecuciones de GitHub con el `GITHUB_TOKEN` de alcance `actions: read` para obtener `run_started_at` y `updated_at`. Si esa consulta no está disponible, usa timestamps de respaldo y duración `0`; sigue enviando el evento si OpenObserve está configurado. El token no forma parte del evento.

## Manejo de fallos y seguridad

- Sin configuración completa: se registra un mensaje seguro y no se envía HTTP.
- Error al leer metadatos de GitHub: se usa un fallback sin cancelar el informe.
- Error HTTP o de red de OpenObserve: se registra sólo el estado HTTP (cuando existe) o un mensaje genérico; no se imprimen URL, encabezados, credenciales ni cuerpo de error.
- La solicitud usa `Content-Type: application/json` y autenticación Basic formada en memoria. Se recomienda HTTPS y una cuenta de ingesta con el permiso mínimo.
- Los valores proceden de contextos nativos de GitHub y `JSON.stringify` serializa/escapa los campos, sin interpolarlos manualmente en JSON.

## Verificación pendiente en OpenObserve

Después de configurar una instancia real, confirme primero el stream y sus columnas. Las siguientes consultas son plantillas y deben adaptarse al stream y dialecto disponibles:

```sql
SELECT _timestamp, workflow_name, workflow_run_id, status, deployment_status,
       branch, commit_sha, duration_seconds
FROM "<pipeline-stream>"
WHERE event_type = 'ci_cd_pipeline'
ORDER BY _timestamp DESC
LIMIT 100
```

```sql
SELECT status, count(*) AS executions
FROM "<pipeline-stream>"
WHERE event_type = 'ci_cd_pipeline'
GROUP BY status
```

Conserve evidencia de una ejecución exitosa, una fallida y una con despliegue omitido antes de declarar la integración real confirmada.

## Dashboards y alertas propuestos

No se crearon dashboards ni alertas por API o UI porque no se proporcionó una instancia real.

| Dashboard | Paneles sugeridos |
|---|---|
| Ejecuciones por estado | Conteo de `status` por período; success/failure/cancelled/skipped. |
| Historial de pipelines | Tabla con run URL, rama, SHA, actor, estado y despliegue. |
| Duración | Serie y percentiles de `duration_seconds`; segmentar por rama. |
| Fallos por rama/etapa | Conteos de `status=failure` por `branch`, `test_status`, `publish_status` y `deployment_status`. |
| Despliegues | Éxitos, fallos y omitidos desde `deployment_status`. |

Alertas iniciales, ajustables y agrupadas:

| Alerta | Condición inicial | Ventana |
|---|---|---|
| Pipeline fallido | Al menos un evento `status=failure` | 5 min |
| Fallos consecutivos | Tres o más fallos en la misma rama | 60 min |
| Despliegue fallido | Al menos un `deployment_status=failure` | 15 min |

Las alertas deben dirigirse a los responsables definidos por el equipo y no sustituyen las notificaciones nativas de GitHub Actions.

## Límites

- GitHub puede no iniciar el job final si una cancelación interrumpe todo el workflow; `always()` cubre los casos en que GitHub sí permite programarlo.
- La obtención del tiempo depende de la API de GitHub Actions; existe fallback, pero no permite reconstruir el inicio real si la API no responde.
- URL, organización, stream y credenciales de OpenObserve siguen sin verificarse. La integración está preparada, no confirmada contra OpenObserve.
