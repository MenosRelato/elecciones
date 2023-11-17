# Control de Elecciones Argentinas

Este proyecto intenta proveer herramientas y reportes para ayudar a controlar 
los comicios en Argentina. 

Para eso, utiliza la información disponible públicamente en https://resultados.gob.ar/ y 
https://datos.gob.ar/ (actualmente, los resultados para [Elecciones 2023](https://datos.gob.ar/dataset/dine-resultados-provisionales-elecciones-2023).

Resumen de deteccion de anomalias:

![Anomalias](https://github.com/MenosRelato/elecciones/blob/main/assets/anomalias.png?raw=true)

La detección de anomalías se efectua utilizando el [rango intercuartílico](https://barcelonageeks.com/rango-intercuartilico-para-detectar-valores-atipicos-en-los-datos/) 
(IQR) del % de votos obtenido por un partido en una mesa, y se reportan las mesas que se 
encuentran fuera de ese rango, ya sea dentro de un mismo establecimiento, dentro del 
circuito o la sección electoral.


# Instalación

La herramienta principal es una aplicación de consola que utiliza [.NET](https://get.dot.net), 
y se puede instalar (o actualizar) desde una consola con el siguiente comando:

```
dotnet tool update -g dotnet-elecciones --add-source https://menosrelato.blob.core.windows.net/nuget/index.json
```

# Uso

Ejecutar `elecciones -h` para obtener ayuda, similar a:

```
❯ elecciones -h
USAGE:
    elecciones [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help       Prints help information
    -v, --version    Prints version information

COMMANDS:
    anomaly     Detectar anomalias en los telegramas de la eleccion
    dataset     Descargar el dataset CSV completo de datos.gob.ar
    download    Descargar los resultados en formato JSON y telegramas con su metadata.
                Opcionalmente filtrar por distrito(s)
    slice       Crear un subset de datos para uno o mas distritos, en formato JSON o CSV
```

La herramienta almacena los datos descargados en `%appdata%\MenosRelato\elecciones`.

## dataset

Este comando descarga de dataset comprimido de https://datos.gob.ar/dataset/dine-resultados-provisionales-elecciones-2023, 
y lo descomprime en `%appdata%\MenosRelato\elecciones\dataset\csv`.
Los archivos contienen toda la información publicada al momento de ejecutar 
el comando, y puede volver a ejecutarse para descargar información actualizada 
si hubiera.

Este comando se complementa con `slice` con el que puede filtrarse de manera 
más eficiente la información a procesar, dado que el archivo completo es 
demasiado grande para procesarlo en su totalidad (con Excel o PowerBI, por ejemplo).

## download

Este comando descarga los resultados en formato JSON y telegramas con su metadata
ya procesados por Menos Relato en base al `dataset`. Esto facilita el procesamiento 
de los datos, ya que se encuentran en un formato más amigable. 

El formato JSON de resultados contiene la misma informacion que el dataset, pero 
organizado jerarquicamente por distrito/seccion/circuito/mesa, y con la direccion 
de la imagen del telegrama correspondiente, asi como la pagina web de resultados.gob.ar
donde se puede ver el telegrama original.

```
❯ dotnet run -- download -h
DESCRIPTION:
Descargar los resultados en formato JSON y telegramas con su metadata.
Opcionalmente filtrar por distrito(s).

USAGE:
    elecciones download [OPTIONS]

EXAMPLES:
    elecciones download --district 21

OPTIONS:
                               DEFAULT
    -h, --help                            Prints help information
    -e, --election             GENERAL    Tipo de eleccion a cargar
    -y, --year                 2023       Año de la eleccion a cargar
        --proxy                           Utilizar un proxy para HTTP
    -s, --storage                         Conexion de Azure Storage a usar (connection string)
    -d, --district <VALUES>               Distrito(s) a incluir en la descarga de telegramas
    -r, --results                         Descargar solo los resultados, no los telegramas
    -o, --open                 True       Abrir la carpeta de descarga al finalizar
```

## slice

Este comando crea un subset de datos para uno o mas distritos, en formato JSON o CSV.

```
❯ dotnet run -- slice -h
DESCRIPTION:
Crear un subset de datos para uno o mas distritos, en formato JSON o CSV.

USAGE:
    elecciones slice [OPTIONS]

EXAMPLES:
    elecciones slice --format csv --district 2 --district 21

OPTIONS:
                               DEFAULT
    -h, --help                            Prints help information
    -e, --election             GENERAL    Tipo de eleccion a cargar
    -y, --year                 2023       Año de la eleccion a cargar
        --proxy                           Utilizar un proxy para HTTP
    -d, --district <VALUES>               Distrito(s) a incluir en el subset
    -o, --output                          Archivo de salida con el subset de datos. Por defecto 'slice.json' (o '.csv')
    -f, --format               Json       Formato del archivo a generar. Opciones: Json, Csv
```

## anomaly

Este comando detecta anomalias en los telegramas de la eleccion. 
Si los telegramas no fueron previamente descargados, los descarga, 
al igual que con el archivo de resultados (`download -r` y `telegram -d`).

Las anomalias se reportan en `%appdata%\MenosRelato\elecciones`.

## Avanzado

Los comandos avanzados estan ocultos por defecto, y se pueden habilitar con la 
opcion `--advanced`. Pueden utilizarse para recrear localmente todos los datos, 
incluyendo la descarga de telegramas del sitio original de resultados.gob.ar.

## prepare

Este comando utiliza el dataset descargado para popular un archivo local normalizado 
para su posterior procesamiento para calcular estadisticas. 

```
❯ dotnet run -- prepare -h
DESCRIPTION:
Preparar el dataset completo de resultados en formato JSON

USAGE:
    elecciones prepare [OPTIONS]

OPTIONS:
                      DEFAULT
    -h, --help                   Prints help information
    -e, --election    GENERAL    Tipo de eleccion a cargar
    -y, --year        2023       Año de la eleccion a cargar
        --proxy                  Utilizar un proxy para HTTP
```

Si no se encuentran datos descargados, este comando preguntará si desea descargarlos 
antes de continuar.

## telegram

Este comando descarga los telegramas del sitio original de resultados.gob.ar,

```
❯ dotnet run -- telegram -h --advanced
DESCRIPTION:
Descarga telegramas de la eleccion con metadata en formato JSON (compactado con GZip)

USAGE:
    elecciones telegram [OPTIONS]

OPTIONS:
                      DEFAULT
    -h, --help                   Prints help information
    -e, --election    GENERAL    Tipo de eleccion a cargar
    -y, --year        2023       Año de la eleccion a cargar
        --proxy                  Utilizar un proxy para HTTP
    -s, --skip                   # de distritos a saltear
    -t, --take                   # de distritos a procesar
    -p, --paralell               # de items a procesar en paralelo
```