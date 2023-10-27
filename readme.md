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
    prepare     Preparar el dataset completo de resultados en formato JSON
```

La herramienta almacena los datos descargados en `%appdata%\MenosRelato\elecciones`.


## download

Este comando descarga de dataset comprimido de https://datos.gob.ar/dataset/dine-resultados-provisionales-elecciones-2023, y lo descomprime en `%appdata%\MenosRelato\elecciones\datos\csv` para su uso posterior. 
Los archivos contienen toda la información publicada al momento de ejecutar 
el comando, y puede volver a ejecutarse para descargar información actualizada 
si hubiera.

## prepare

Este comando utiliza el dataset descargado para popular un archivo local normalizado 
para su posterior procesamiento para calcular estadisticas. Para obtener ayuda, 
ejecutar `elecciones prepare -h`:

```
❯ elecciones prepare -h
USAGE:
    elecciones prepare [OPTIONS]

OPTIONS:
                  DEFAULT
    -h, --help               Prints help information
    -y, --year    2023       Año de la eleccion a cargar
    -k, --kind    General    Tipo de eleccion a cargar
    -j, --json               Crear un archivo JSON de texto legible
    -z, --zip                Comprimir el JSON de texto legible con GZip
```

Si no se encuentran datos descargados, este comando preguntará si desea descargarlos 
antes de continuar.

Si se especifica `-j` (o `--json`), se creara un archivo en formato JSON de texto 
legible (e indentado) en `%appdata%\MenosRelato\elecciones` para su procesamiento 
con otras herramientas. 

<details>
<summary>Expandir ejemplo</summary>

```json
{
  "Year": 2023,
  "Kind": 1,
  "Ballots": {
    "0": "POSITIVO",
    "1": "EN BLANCO",
    "2": "NULO",
    "3": "RECURRIDO",
    "4": "IMPUGNADO",
    "5": "COMANDO"
  },
  "Parties": [
    {
      "Id": 134,
      "Name": "UNION POR LA PATRIA"
    },
    {
      "Id": 132,
      "Name": "JUNTOS POR EL CAMBIO"
    },
    {
      "Id": 135,
      "Name": "LA LIBERTAD AVANZA"
    },
    {
      "Id": 136,
      "Name": "FRENTE DE IZQUIERDA Y DE TRABAJADORES - UNIDAD"
    },
    {
      "Id": 133,
      "Name": "HACEMOS POR NUESTRO PAIS"
    }
  ],
  "Positions": [
    {
      "Id": 1,
      "Name": "PRESIDENTE Y VICE"
    }
  ],
  "Districts": [
    {
      "Id": 1,
      "Name": "Ciudad Autónoma de Buenos Aires",
      "Provincials": [
        {
          "Sections": [
            {
              "Id": 1,
              "Name": "Comuna 01",
              "Circuits": [
                {
                  "Id": "00018",
                  "Name": "00018",
                  "Booths": [
                    {
                      "Id": 475,
                      "Electors": 349,
                      "Ballots": [
                        {
                          "Count": 95,
                          "Position": 1,
                          "Party": 134
                        },
                        {
                          "Count": 59,
                          "Position": 1,
                          "Party": 132
                        },
                        {
                          "Count": 57,
                          "Position": 1,
                          "Party": 135
                        },
                        {
                          "Count": 9,
                          "Position": 1,
                          "Party": 136
                        },
                        {
                          "Count": 4,
                          "Position": 1,
                          "Party": 133
                        },
                        {
                          "Kind": 1,
                          "Count": 4,
                          "Position": 1
                        }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
      ]
    }
  ]
}
```

</details>

