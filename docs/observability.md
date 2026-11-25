# Observabilidad OTLP

La instrumentación se mantiene activa localmente, pero la exportación OTLP sólo se habilita cuando se configura un endpoint válido. No se incluyen endpoints ni credenciales en el repositorio.

## Variables de entorno

| Variable | Uso |
|---|---|
| `OTEL_SERVICE_NAME` | Nombre del servicio. Predeterminado: `WebApp`. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Endpoint OTLP común; se usa como fallback para las tres señales. Adecuado para gRPC o un Collector que acepte señales en una misma URI. |
| `OTEL_EXPORTER_OTLP_TRACES_ENDPOINT` | Endpoint OTLP específico de trazas; tiene prioridad sobre el endpoint común. |
| `OTEL_EXPORTER_OTLP_METRICS_ENDPOINT` | Endpoint OTLP específico de métricas; tiene prioridad sobre el endpoint común. |
| `OTEL_EXPORTER_OTLP_LOGS_ENDPOINT` | Endpoint OTLP específico de logs; tiene prioridad sobre el endpoint común. |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` o `http/protobuf`. Predeterminado: `grpc`. |
| `OTEL_EXPORTER_OTLP_HEADERS` | Headers OTLP en el formato del exporter; puede contener autenticación y debe entregarse sólo como secreto de entorno. |
| `OTEL_RESOURCE_ATTRIBUTES` | Atributos de recurso estándar de OpenTelemetry. |
| `OTEL_METRIC_EXPORT_INTERVAL` | Intervalo de métricas en milisegundos; entre 1000 y 3600000. Predeterminado: 60000. |

## Conexión con OpenObserve

1. El administrador de OpenObserve debe proporcionar las URL OTLP exactas —una por señal cuando HTTP las requiera—, protocolo y, si corresponde, el header de autenticación compatible con su instalación.
2. Configure esas variables en el entorno de ejecución o en el gestor de secretos del despliegue; no en `appsettings.json`, Dockerfile ni repositorio.
3. Seleccione `grpc` o `http/protobuf` según el endpoint entregado. La aplicación no agrega ni transforma rutas: para OTLP/HTTP directo, configure los tres endpoints específicos entregados por OpenObserve.
4. Reinicie la aplicación. Si el endpoint no está configurado, WebApp conserva trazas, métricas y logs locales sin exportarlos.

Los errores temporales del exporter se procesan en segundo plano y no bloquean solicitudes, autenticación ni operaciones CRUD.

Las métricas `webapp.http.server.requests` y `webapp.http.server.successes`/`webapp.http.server.errors` tienen etiquetas normalizadas de método, ruta y código HTTP. Las solicitudes por segundo se calculan en el backend de observabilidad a partir de `webapp.http.server.requests` y su intervalo, no se emiten como un contador acumulado independiente.
