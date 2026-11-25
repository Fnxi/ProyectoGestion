# Integración y dashboards de OpenObserve

> **Estado:** configuración preparada y validada con un colector OTLP local en las pruebas automatizadas. **No** se ha conectado ni validado contra una instancia real de OpenObserve porque no se proporcionaron URL, organización, streams ni credenciales.

## Auditoría de la instrumentación de WebApp

| Señal | Fuente / nombre real | Atributos seguros relevantes |
|---|---|---|
| Métricas HTTP | Meter `WebApp.Observability` | `http.request.method`, `http.route`, `http.response.status_code` |
| Solicitudes | `webapp.http.server.requests` | Los anteriores |
| Éxitos | `webapp.http.server.successes` | Los anteriores |
| Errores HTTP | `webapp.http.server.errors` | Los anteriores |
| Duración HTTP | `webapp.http.server.duration` (ms) | Los anteriores |
| Trazas internas | Activity source `WebApp.Application`, actividad `webapp.http.request` | Método, ruta, estado HTTP y tipo de error cuando aplica |
| Trazas automáticas | ASP.NET Core, HttpClient y Entity Framework Core | Contexto W3C TraceId/SpanId y operación de base de datos |
| Logs | `ILogger` + provider OpenTelemetry | Nivel, mensaje estructurado, scopes, TraceId y SpanId |

Las rutas se toman de `RouteEndpoint.RoutePattern`, por lo que `/api/products/{id}` no genera cardinalidad por ID. No se envían cuerpos HTTP, cookies, tokens, encabezados de autorización, contraseñas, correos ni parámetros EF. La captura experimental de parámetros de EF permanece deshabilitada por defecto.

## Streams y recursos

OpenObserve separa las señales en sus flujos de **logs**, **metrics** y **traces**. Los nombres concretos de stream, organización y retención dependen de la instancia y deben confirmarse en **Data Sources / Streams** después de la primera recepción.

Recursos que WebApp añade:

- `service.name`: `OTEL_SERVICE_NAME` o `WebApp`.
- `deployment.environment.name`: entorno ASP.NET Core.
- atributos adicionales de `OTEL_RESOURCE_ATTRIBUTES`.

Antes de crear paneles, registre los nombres reales mostrados por Metrics Explorer y los streams reales de logs y traces. OpenObserve puede normalizar nombres/etiquetas de métricas durante la ingesta; no se presupone una conversión de puntos a guiones bajos.

## Conexión OTLP

### Recomendación

Para una conexión directa desde .NET, prefiera **OTLP/HTTP Protobuf** si la instancia lo ofrece: suele atravesar proxies con mayor facilidad. Para alto volumen y una red con HTTP/2 verificado, gRPC es una alternativa.

La documentación de OpenObserve indica que las instalaciones autogestionadas suelen exponer OTLP/HTTP en 4318 y gRPC en 5081, pero esos valores son configurables. No use esos puertos ni rutas hasta que el administrador de la instancia los confirme.

### Variables requeridas

Configure secretos exclusivamente en el entorno de ejecución o gestor de secretos:

```text
OTEL_SERVICE_NAME=<nombre-de-servicio-aprobado>
OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
OTEL_EXPORTER_OTLP_TRACES_ENDPOINT=<url-exacta-de-traces>
OTEL_EXPORTER_OTLP_METRICS_ENDPOINT=<url-exacta-de-metrics>
OTEL_EXPORTER_OTLP_LOGS_ENDPOINT=<url-exacta-de-logs>
OTEL_EXPORTER_OTLP_HEADERS=<headers-entregados-por-el-administrador>
OTEL_RESOURCE_ATTRIBUTES=deployment.environment.name=<entorno>,service.version=<version>
OTEL_METRIC_EXPORT_INTERVAL=60000
```

Para gRPC o un OpenTelemetry Collector que acepte las tres señales en un único endpoint, se puede usar `OTEL_EXPORTER_OTLP_ENDPOINT` en lugar de los tres endpoints específicos. Los endpoints por señal tienen prioridad.

No se incluyen valores de organización, stream, URL, `Authorization` ni token de ejemplo: deben proceder de la instancia real. Si OpenObserve exige un header de organización o `stream-name`, inclúyalo sólo dentro de `OTEL_EXPORTER_OTLP_HEADERS` según la documentación y política de esa instancia.

## Procedimiento de recepción

1. El administrador crea o confirma la organización, permisos de ingesta y streams para logs, métricas y trazas.
2. Configure las variables anteriores como secretos y reinicie una instancia no productiva.
3. Genere: una solicitud a `/api/health`, un login correcto, un login fallido, una operación administrativa y una operación de producto.
4. En OpenObserve, filtre por `service.name` y `deployment.environment.name`.
5. Confirme que existen los tres tipos de señal, guarde sus nombres de stream y valide que TraceId enlaza logs y trazas.
6. Compruebe que no aparecen contraseñas, cookies, tokens, Authorization, correos ni parámetros SQL.

La evidencia mínima a conservar es una captura o enlace de cada stream, una consulta guardada por señal y un TraceId donde se vea la solicitud HTTP con su span EF hijo. Sin esa evidencia, la integración debe considerarse **preparada, no confirmada**.

## Consultas de verificación

Use los nombres de stream y de métrica resueltos en la UI. Los siguientes son plantillas, no valores ejecutables hasta reemplazar `<...>`:

### Logs (SQL)

```sql
SELECT _timestamp, level, body, trace_id, span_id
FROM "<log-stream>"
WHERE service_name = '<service-name>'
ORDER BY _timestamp DESC
LIMIT 100
```

Busque los mensajes estructurados `Application starting.`, `Authentication succeeded.`, `Authentication failed due to invalid credentials.`, `Administrative user ...` y `Unhandled request failure.`. Los nombres exactos de columnas se deben seleccionar desde el explorador del stream, pues dependen del mapeo de la instancia.

### Métricas (PromQL)

Tras localizar en Metrics Explorer el identificador almacenado de `webapp.http.server.requests`, use:

```promql
sum(increase(<requests_metric>{service_name="<service-name>"}[5m]))
```

Para validar éxitos, errores y duración, sustituya respectivamente `<successes_metric>`, `<errors_metric>` y `<duration_metric>` por los instrumentos almacenados correspondientes a los cuatro nombres auditados arriba. Verifique en el selector de etiquetas cómo se representa `http.response.status_code` antes de filtrar 5xx.

### Trazas

En Trace Explorer filtre `service.name = <service-name>`. Abra una traza de `webapp.http.request`, compruebe TraceId/SpanId, duración y estado; después confirme los spans de ASP.NET Core y Entity Framework Core asociados. No se debe buscar por email, usuario ni ID de entidad.

## Dashboards manuales

No se crearon scripts ni recursos por API: no se verificó una API de dashboards/alertas contra la versión real de OpenObserve.

### 1. Aplicación

| Panel | Señal | Consulta / visualización |
|---|---|---|
| Solicitudes totales | Metrics | `increase(<requests_metric>[periodo])`, tarjeta. |
| RPS | Metrics | `sum(rate(<requests_metric>[5m]))`, serie temporal. |
| Éxitos y fallos | Metrics | Series separadas de successes y errors. |
| Latencia media | Metrics | `avg(<duration_metric>)`, serie temporal. |
| Latencia p95 | Metrics | `histogram_quantile(0.95, ...)` sólo después de confirmar en Metrics Explorer que el histograma y sus buckets están disponibles. |
| Códigos HTTP | Metrics | Agrupar por la etiqueta de estado verificada. |
| Evolución temporal | Metrics | Requests, errors y duración en el mismo rango temporal. |

### 2. Rendimiento

| Panel | Señal | Visualización |
|---|---|---|
| Latencia HTTP | Metrics | Percentiles y media de duración. |
| Endpoints lentos | Metrics | Agrupar duración por `http.route`; ordenar descendente. |
| Distribución | Metrics | Histograma de duración si el stream mantiene buckets. |
| Solicitudes por endpoint | Metrics | `rate` agrupado por `http.route`. |
| Operaciones de BD | Traces | Spans EF por duración y operación, sin SQL ni parámetros. |
| Trazas largas | Traces | Ordenar por duración y filtrar servicio/entorno. |

### 3. Seguridad y errores

| Panel | Señal | Visualización |
|---|---|---|
| Fallos de autenticación | Logs | Conteo del mensaje de fallo, por tiempo; sin correo. |
| Errores HTTP | Metrics | `errors` agrupado por ruta y estado. |
| Excepciones | Logs / traces | Conteo de `Unhandled request failure.` y spans con error. |
| Operaciones administrativas | Logs | Conteo de mensajes `Administrative user ...`. |
| Tendencia de errores | Metrics | Serie temporal de errores, distinguiendo 4xx y 5xx cuando la etiqueta esté confirmada. |

## Alertas iniciales

Configure alertas programadas desde la UI, empezando en un entorno no productivo. Ajuste los umbrales con tráfico real.

| Alerta | Condición inicial | Ventana | Nota |
|---|---|---|---|
| 5xx sostenidos | Más de 5 respuestas 5xx **y** más del 2% de solicitudes | 5 min | Excluir 4xx esperados. Confirmar el filtro de etiqueta antes de activar. |
| Latencia alta | p95 HTTP mayor de 1 s | 10 min | Sólo si el histograma permite percentiles en la instancia. |
| Sin telemetría | No hay solicitudes ni heartbeat esperado | 15 min | Activar sólo si existe tráfico esperado; evita alertas nocturnas falsas. |
| Errores de aplicación | 3 o más eventos `Unhandled request failure.` | 5 min | Agrupar por ventana, no alertar por un único evento. |
| Fallo de exportación | Error repetido confirmado en logs del proceso/colector | 10 min | Puede no llegar a OpenObserve si el destino está caído; vigilar el runtime o Collector. |

No se alerta por fallos individuales de autenticación ni por 4xx ordinarios. Las notificaciones, destinatarios y canales los debe definir el administrador de OpenObserve.

## Fuentes consultadas

- OpenObserve: documentación de ingesta OTLP de logs, métricas y trazas.
- OpenObserve: Metrics Explorer, dashboards y alertas programadas.
- Instrumentación real de `Observability/` en este repositorio.
