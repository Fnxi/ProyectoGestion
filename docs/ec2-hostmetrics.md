# Monitoreo de infraestructura EC2 con OpenTelemetry Collector Contrib

> **Estado:** configuración preparada y no ejecutada. No hubo conexión a EC2, SSH, instalación, reinicio, despliegue ni recepción comprobada en OpenObserve.

## Auditoría del repositorio

| Artefacto | Hallazgo verificado |
|---|---|
| `Dockerfile` | Imagen de WebApp .NET 10, expone 80; no contiene Collector. |
| `scripts/deploy-ec2.sh` | Blue/green local en 8081/8082, sólo publicado en `127.0.0.1`; Nginx se genera en tiempo de despliegue. |
| Nginx versionado | No existe archivo de configuración independiente en el repositorio; el script crea `/etc/nginx/conf.d/webapp-api.conf`. |
| GitHub Actions | Construye/publica/despliega WebApp por SSH. No fue modificado. |
| Persistencia | El volumen Docker `webapp-api-data` monta `/data`; SQLite está en `/data/webapp.db`. |
| Puertos | Nginx escucha 80; no hay puertos del Collector configurados actualmente. |

El agente definido en `infrastructure/otelcol/` es independiente: no modifica el Dockerfile, Nginx, workflow, script blue/green ni volumen de datos de WebApp.

## Arquitectura propuesta

```text
Ubuntu EC2 host
  ├─ Nginx :80 → WebApp blue/green :8081/:8082
  └─ otelcol-contrib hostmetrics (sin puertos publicados)
       └─ OTLP/HTTP saliente → endpoint de métricas OpenObserve confirmado por el administrador
```

El agente usa `hostmetrics`, `resource`, `batch` y `otlphttp`. No recibe telemetría de WebApp y no duplica su instrumentación; sólo recopila métricas del sistema operativo.

## Configuración preparada

- Imagen fijada: `otel/opentelemetry-collector-contrib:0.165.1`.
- Configuración: `infrastructure/otelcol/hostmetrics-config.yaml`.
- Ejecución aislada: `infrastructure/otelcol/docker-compose.hostmetrics.yaml`.
- Sin `ports`, Docker socket, `privileged`, `pid: host` ni montaje de escritura del host.
- Sistema de archivos del Collector en modo `read_only`; capacidades Linux eliminadas con `cap_drop: ALL`.
- Sólo se montan `/`, `/proc` y `/sys` del host en modo lectura bajo `/hostfs` para que `root_path: /hostfs` lea las métricas del host y no del contenedor.

El proceso predeterminado del Collector se ejecuta como root **dentro del contenedor** para maximizar la lectura de los pseudo-filesystems montados. Esto no concede `privileged`, capacidades Linux adicionales, Docker socket ni escritura sobre los montajes. Antes de instalar, el operador debe validar si la instancia permite ejecutar el Collector como un UID no root sin perder las métricas necesarias.

## Variables de entorno del agente

| Variable | Propósito |
|---|---|
| `OTEL_SERVICE_NAME` | Nombre lógico del agente, por ejemplo el aprobado por operaciones. |
| `DEPLOYMENT_ENVIRONMENT` | Entorno de ejecución. |
| `EC2_INSTANCE_NAME` | Identificador no sensible administrado manualmente para `host.name`; no se consulta metadata EC2 ni se recoge IP. |
| `OPENOBSERVE_OTLP_METRICS_ENDPOINT` | Endpoint OTLP/HTTP exacto de métricas confirmado por OpenObserve. |
| `OPENOBSERVE_OTLP_AUTHORIZATION` | Valor de Authorization entregado como secreto por el administrador. |
| `OPENOBSERVE_ORGANIZATION` | Organización real de la instancia. |
| `OPENOBSERVE_METRICS_STREAM` | Stream de métricas real confirmado en OpenObserve. |

No cree un archivo `.env` versionado. Proporcione estas variables desde un gestor de secretos o una sesión administrativa protegida. El endpoint, organización, header y stream no se conocen en este repositorio.

## Métricas emitidas por hostmetrics

Los nombres siguientes proceden de la documentación del hostmetrics receiver para la versión fijada y deben confirmarse en Metrics Explorer tras la primera recepción.

### CPU

| Métrica | Unidad | Uso |
|---|---:|---|
| `system.cpu.logical.count` | `{cpu}` | Procesadores lógicos disponibles. |
| `system.cpu.time` | `s` acumulados | Tiempo por atributo `state` (`user`, `system`, `idle`, `wait`, etc.). |
| `system.cpu.utilization` | fracción `[0,1]` | Uso por CPU y estado; multiplicar por 100 sólo para presentarlo como porcentaje. |

La utilización es una medida entre intervalos; no sume valores acumulados de `system.cpu.time` como si fueran porcentajes.

### Memoria

| Métrica | Unidad | Uso |
|---|---:|---|
| `system.memory.limit` | `By` | RAM total disponible para el sistema. |
| `system.memory.usage` | `By` | Uso por `state`, incluido `used`, `free`, `cached` y `buffered`. |
| `system.linux.memory.available` | `By` | Estimación Linux de memoria disponible para nuevas aplicaciones. |
| `system.memory.utilization` | fracción `[0,1]` | Porcentaje de uso por estado cuando está habilitada. |

En Linux, `free` no equivale a capacidad utilizable: `available` considera caché recuperable y es el indicador recomendado para capacidad. No clasifique caché como agotamiento de memoria sin revisar los estados.

### Red

| Métrica | Unidad | Atributos |
|---|---:|---|
| `system.network.io` | `By` acumulados | `device`, `direction` (`receive`/`transmit`) |
| `system.network.packets` | `{packets}` acumulados | `device`, `direction` |
| `system.network.errors` | `{errors}` acumulados | `device`, `direction` |
| `system.network.dropped` | `{packets}` acumulados | `device`, `direction` |

La interfaz `lo` se excluye. Bytes/s y paquetes/s se calculan mediante `rate` o diferencia por intervalo; los valores acumulados no son velocidades instantáneas. No se recopilan direcciones IP.

### Disco y sistema

| Métrica | Unidad | Uso |
|---|---:|---|
| `system.filesystem.usage` | `By` | Espacio por `mountpoint`, `type` y estado `used`/`free`/`reserved`. |
| `system.filesystem.utilization` | fracción `[0,1]` | Uso porcentual de filesystem. |
| `system.uptime` | `s` | Tiempo activo del sistema. |

Se excluyen por defecto `tmpfs`, `devtmpfs`, `overlay` y `squashfs` para reducir ruido de pseudo-filesystems y capas de contenedor. No se habilitó el scraper de procesos ni el Docker socket.

## Temperatura

No se configura temperatura. Las instancias EC2 son virtualizadas y normalmente no exponen sensores térmicos utilizables a Ubuntu o al contenedor. El hostmetrics receiver no ofrece una métrica térmica portátil para este escenario. Sólo se evaluaría tras comprobar sensores reales y permisos en la instancia; no se generan valores ficticios.

## Instalación manual propuesta

Estos pasos son para un operador de EC2 y **no fueron ejecutados**:

1. Obtener la configuración desde una fuente aprobada y colocar ambos archivos de `infrastructure/otelcol/` en un directorio protegido de la instancia.
2. Verificar por el canal de OpenObserve la URL OTLP/HTTP de métricas, organización, stream y mecanismo de Authorization.
3. Proveer las siete variables requeridas mediante gestor de secretos o entorno protegido; no guardarlas en el repositorio.
4. Validar la configuración con la imagen fijada antes de arrancar el servicio:

   ```bash
   docker run --rm \
     -v "$PWD/hostmetrics-config.yaml:/etc/otelcol-contrib/hostmetrics-config.yaml:ro" \
     otel/opentelemetry-collector-contrib:0.165.1 \
     validate --config=/etc/otelcol-contrib/hostmetrics-config.yaml
   ```

5. Revisar los logs iniciales del Collector, confirmar que no publica puertos y arrancarlo con el compose preparado sólo tras aprobación operativa.
6. En OpenObserve, validar los atributos `service.name`, `deployment.environment.name`, `host.name`, `cloud.provider=aws` y `cloud.platform=aws_ec2` antes de crear paneles.

La sintaxis `validate` debe confirmarse contra la imagen fijada en la instancia antes de considerarla un control de despliegue; en esta estación Docker no tenía daemon disponible para ejecutar esa validación.

## Verificación en OpenObserve

1. Localice el stream real indicado por `OPENOBSERVE_METRICS_STREAM`.
2. Filtre por `service.name` y `host.name` aprobados.
3. Confirme `system.cpu.utilization`, `system.linux.memory.available`, `system.network.io`, `system.filesystem.usage` y `system.uptime`.
4. Compruebe unidades y atributos reales antes de guardar consultas. OpenObserve puede normalizar nombres de métricas o etiquetas.
5. Conserve evidencia de recepción. Sin ella, el monitoreo permanece preparado pero no probado en EC2.

## Dashboards propuestos

Los paneles deben construirse desde Metrics Explorer después de resolver los nombres almacenados. Use `<metric>` y `<labels>` como placeholders; no copie las expresiones sin ajustar las etiquetas reales.

### EC2 — CPU

- Utilización: promedio de `system.cpu.utilization` por host, mostrado como `100 * valor`.
- CPU por estado: agrupar por `state`.
- CPUs lógicas: último valor de `system.cpu.logical.count`.
- Tendencia: serie temporal, no suma acumulada de `system.cpu.time`.

### EC2 — Memoria

- Total: último `system.memory.limit`.
- Usada: `system.memory.usage{state="used"}`.
- Disponible: `system.linux.memory.available`.
- Porcentaje: `system.memory.utilization{state="used"} * 100` o `used / limit * 100` si la primera no está disponible.

### EC2 — Red

- Entrada: `rate(system.network.io{direction="receive"}[5m])` por `device`.
- Salida: `rate(system.network.io{direction="transmit"}[5m])` por `device`.
- Paquetes, errores y descartes: `rate` de las métricas homónimas, separadas por dirección.
- Excluir `lo`; no filtrar por IP.

### EC2 — Sistema

- Uso de filesystem por `mountpoint`: `system.filesystem.utilization * 100`.
- Espacio libre: `system.filesystem.usage{state="free"}`.
- Uptime: último `system.uptime`.

## Alertas iniciales ajustables

| Alerta | Condición inicial | Ventana | Límite |
|---|---|---|---|
| CPU alta | Uso promedio > 85% | 5 min | Ignora picos cortos; valor de métrica es fracción, comparar contra `0.85` si no se multiplica. |
| RAM alta | Uso `used / limit` > 90% | 5 min | Preferir `available` para investigar presión real. |
| Disco bajo | Espacio libre < 15% del total | 10 min | Excluir pseudo-filesystems. |
| Errores de red | `rate(system.network.errors[5m])` sostenido por encima de baseline | 10 min | Definir baseline tras tráfico real. |
| Agente ausente | Sin muestra reciente esperada | 15 min | Usar sólo cuando se espere actividad continua. |

No se alerta por un único pico. Los umbrales deben recalibrarse tras observar carga, tamaño de instancia y patrón de tráfico reales.

## Limitaciones y pendientes

- La imagen y configuración no se ejecutaron en EC2.
- No se verificaron endpoint, headers, organización ni stream reales de OpenObserve.
- Hostmetrics es beta y varias métricas son de estabilidad Development según su documentación.
- La temperatura no está disponible de forma confiable en el escenario EC2 actual.
- No hay monitoreo de procesos, contenedores, Docker socket, EC2 metadata, CPU/RAM de aplicación ni red por IP.
- No se modificaron Dockerfile, despliegue blue/green, Nginx, GitHub Actions ni AWS.
