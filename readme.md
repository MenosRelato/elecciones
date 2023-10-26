# Control de Elecciones Argentinas

Este proyecto intenta proveer herramientas y reportes para ayudar a controlar 
los comicios en Argentina. 

Para eso, utiliza la información disponible públicamente en https://resultados.gob.ar/ y 
https://datos.gob.ar/ (actualmente, los resultados para [Elecciones 2023](https://datos.gob.ar/dataset/dine-resultados-provisionales-elecciones-2023).

# Instalación

La herramienta principal actualmente es una aplicación de consola que utiliza [.NET](https://get.dot.net), 
y se puede instalar (o actualizar) desde una consola con el siguiente comando:

```
dotnet tool install -g dotnet-elecciones --add-source https://menosrelato.blob.core.windows.net/nuget/index.json
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
    download    Descargar el dataset completo de resultados
    load        Cargar el dataset completo de resultados a una base de datos SQLite
```

La herramienta almacena los datos descargados en `%appdata%\MenosRelato\elecciones`.

## download

Este comando descarga de dataset comprimido de https://datos.gob.ar/dataset/dine-resultados-provisionales-elecciones-2023, y lo descomprime en `%appdata%\MenosRelato\elecciones\datos\csv` para su uso posterior. 
Los archivos contienen toda la información publicada al momento de ejecutar 
el comando, y puede volver a ejecutarse para descargar información actualizada 
si hubiera.

## load 

Este comando utiliza el dataset descargado para popular una base de datos SQLite 
en `%appdata%\MenosRelato\elecciones\elecciones.db` con la información normalizada 
de los archivos CSV, para su mas fácil consulta y análisis.

SQLite posee varias interfaces gratuitas para explorar datos. Recomendamos 
[SQLiteStudio](https://sqlitestudio.pl/).
