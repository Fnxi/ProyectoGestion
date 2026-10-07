# WebApp API: CI/CD con Docker, GitHub Actions y AWS EC2

API REST de tareas y categorias implementada en ASP.NET Core 10 con SQLite. El proyecto ejecuta pruebas de integracion, recolecta cobertura sobre el proceso .NET, publica una imagen inmutable en Docker Hub y despliega en una instancia EC2.

> Estado del alcance: la API actual expone 11 endpoints. El requisito academico de 60 endpoints requiere ampliar el dominio funcional antes de la entrega final.

## Arquitectura

```text
push / pull request a main
             |
             v
GitHub Actions: restore -> Jest + dotnet-coverage (>= 70%)
             |
             v  (solo push a main)
Docker Hub: <usuario>/webapp-api:latest y :<commit-sha>
             |
             v  (SSH con clave y host verificado)
AWS EC2: Docker blue/green -> Nginx :80 -> API :8081 o :8082
```

El script de despliegue inicia la version candidata en el puerto local alterno, consulta `GET /api/health`, cambia Nginx y recien entonces elimina el contenedor anterior. SQLite se conserva en el volumen Docker `webapp-api-data`.

## Endpoints disponibles

| Metodo | Ruta | Descripcion |
| --- | --- | --- |
| GET | `/api/health` | Estado del servicio |
| GET, POST | `/api/tasks` | Listar y crear tareas |
| GET, PUT, DELETE | `/api/tasks/{id}` | Consultar, actualizar y eliminar una tarea |
| GET, POST | `/api/categories` | Listar y crear categorias |
| DELETE | `/api/categories/{id}` | Eliminar una categoria |
| GET | `/api/categories/{id}/tasks` | Tareas de una categoria |
| GET | `/api/database/backup` | Descargar respaldo SQLite |
| DELETE | `/api/database` | Vaciar los datos |

## Ejecucion local

Requisitos: .NET SDK 10, Node.js 22 o superior y Docker opcional.

```powershell
dotnet restore
npm ci
npm test
dotnet run --no-launch-profile --urls http://localhost:8080
```

Verificar salud:

```powershell
Invoke-RestMethod http://localhost:8080/api/health
```

Construir y ejecutar el contenedor:

```powershell
docker build -t webapp-api:local .
docker run --rm -p 8080:80 -v webapp-api-data:/data -e DatabasePath=/data/webapp.db webapp-api:local
```

## Pipeline

El workflow esta en [`.github/workflows/main.yml`](.github/workflows/main.yml).

1. En cada `push` o `pull_request` hacia `main`, instala .NET y Node, ejecuta las pruebas de integracion y publica `coverage/coverage.cobertura.xml` como artefacto.
2. `dotnet-coverage` recolecta la cobertura del proceso ASP.NET iniciado por Jest. El trabajo falla si la cobertura de lineas es menor de 70%.
3. Solo un `push` exitoso a `main` inicia la publicacion. La imagen recibe las etiquetas `latest` y el SHA exacto del commit.
4. El mismo SHA se despliega por SSH, evitando instalar una etiqueta mutable en produccion.

## GitHub Secrets

Crear estos secretos en **Settings > Secrets and variables > Actions**. No agregarlos en archivos versionados.

| Secreto | Valor |
| --- | --- |
| `DOCKERHUB_USERNAME` | Usuario de Docker Hub |
| `DOCKERHUB_TOKEN` | Personal Access Token de Docker Hub con permiso de escritura |
| `EC2_HOST` | IP publica o DNS de la instancia EC2 |
| `EC2_USER` | Usuario SSH, normalmente `ubuntu` |
| `EC2_SSH_KEY` | Contenido completo de la clave privada `.pem` |
| `EC2_SSH_HOST_KEY` | Linea de `known_hosts` de la EC2, obtenida por un canal confiable |

Para obtener y verificar la huella del host desde una red confiable, usar `ssh-keyscan -H <IP_EC2>` y contrastarla con la huella mostrada en la consola de EC2 antes de guardarla como secreto.

## Preparacion de EC2

Crear una instancia Ubuntu con Security Group que permita `22/tcp` solo desde la red administrativa y `80/tcp` desde los clientes necesarios. En la instancia, una sola vez:

```bash
sudo apt-get update
sudo apt-get install -y docker.io nginx curl
sudo usermod -aG docker "$USER"
sudo systemctl enable --now docker nginx
sudo rm -f /etc/nginx/sites-enabled/default
printf '%s\n' 'ubuntu ALL=(ALL) NOPASSWD: /usr/bin/install -m 644 * /etc/nginx/conf.d/webapp-api.conf, /usr/sbin/nginx -t, /usr/bin/systemctl reload nginx' | sudo tee /etc/sudoers.d/webapp-api-deploy
sudo chmod 440 /etc/sudoers.d/webapp-api-deploy
sudo visudo -cf /etc/sudoers.d/webapp-api-deploy
exit
```

Volver a conectarse por SSH para que el grupo `docker` quede activo. Si el repositorio Docker Hub es privado, autenticar Docker una vez en EC2 con un token de solo lectura. El usuario SSH debe poder ejecutar `sudo nginx` sin solicitar contrasena.

El archivo [`scripts/deploy-ec2.sh`](scripts/deploy-ec2.sh) se envia desde Actions; no requiere clonar el repositorio en la instancia. Para realizar el primer despliegue basta hacer `push` a `main` despues de configurar los secretos.

## Verificacion de produccion

```bash
curl http://<IP_EC2>/api/health
```

La respuesta esperada es `{"statusCode":200,"data":{"status":"healthy"}}`.

## Demostracion

1. Cambiar un mensaje visible de la API y ejecutar `npm test` localmente.
2. Crear un commit y hacer `git push origin main`.
3. Mostrar los trabajos **Tests and coverage** y **Publish image and deploy** en Actions.
4. Mostrar en Docker Hub las etiquetas `latest` y el SHA del commit.
5. Consultar `/api/health` o el endpoint modificado en la IP publica de EC2.

El borrador del informe academico se encuentra en [`report/Reporte.tex`](report/Reporte.tex).
