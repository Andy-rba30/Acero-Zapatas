# Prompt para el instalador de ARBA — añadir el plugin Zapatas

Añade al instalador de los plugins ARBA para Revit 2027 el nuevo add-in **Zapatas**
(armado de zapatas aisladas, combinadas, corridas y losas de cimentación). Es el hermano de
**Losas** (Acero-losas), **Columnas** (Acero-columnas) y **Muros** (Acero-automatico): se
instala exactamente igual y aparece en el mismo sitio de la cinta. No crees ningún botón ni
pestaña desde el instalador: la cinta la crea el propio add-in al arrancar Revit.

## Origen

- Repositorio: https://github.com/Andy-rba30/Acero-Zapatas, rama `main`. Lleva el código común
  ARBA como **submódulo git** (`external/ARBA-comun`, etiqueta `v1.0.0`): clona con
  `git clone --recurse-submodules …` o, en un clon ya hecho, `git submodule update --init`;
  sin el submódulo la compilación falla (faltan las clases `Arba.Comun.*`).
- Proyecto: `FootingRebar.csproj` (.NET 10, `net10.0-windows`, x64). Compilar con
  `dotnet build -c Release`. La salida está en `bin\Release\net10.0-windows\`.
- No tiene dependencias aparte de las DLL de Revit (los paquetes `Nice3point.Revit.Api.*`
  son solo de compilación, no se copian). El código común se compila **dentro** de
  `FootingRebar.dll` (no hay ninguna `Arba.Comun.dll` que instalar). No hace falta copiar nada
  de `bin` salvo `FootingRebar.dll` (y `FootingRebar.pdb` si quieres depurar).

## Archivos a instalar (por usuario, `%AppData%\Autodesk\Revit\Addins\2027\`)

```
%AppData%\Autodesk\Revit\Addins\2027\FootingRebar.addin              <- del repo (raiz)
%AppData%\Autodesk\Revit\Addins\2027\FootingRebar\FootingRebar.dll   <- de bin\Release\net10.0-windows\
%AppData%\Autodesk\Revit\Addins\2027\FootingRebar\config.json        <- del repo (raiz); NO sobrescribir si ya existe
                                                                        (guarda los valores por defecto del usuario)
```

El manifiesto `FootingRebar.addin` ya trae las rutas relativas `FootingRebar\FootingRebar.dll`,
los dos registros (Application `FootingRebar.RibbonApp` con ClientId
`7d4b2f91-3c6a-4e58-9b1d-2a8f6c0e5d73` y Command `FootingRebar.ArmarZapataCommand` con ClientId
`a2c8e6f4-1b9d-4d37-8e5a-6f0c3b7d9e21`), `VendorId` LOCAL. No lo modifiques. Si el
instalador usa una carpeta común para todos los add-ins ARBA en lugar de una por plugin,
ajusta solo la etiqueta `<Assembly>` del manifiesto para que apunte a la ruta real de la
DLL. `config.json` tiene que quedar siempre en la misma carpeta que `FootingRebar.dll`
(el add-in lo busca junto a su ensamblado).

## Dónde aparece en Revit

Pestaña **ARBA** > panel **Acero** > desplegable **Acero** > botón **Zapatas**, junto a
**Columnas**, **Muros**, **Losas** y los demás add-ins instalados. Todos llevan la misma clase
`ArbaRibbon` del código común ARBA: cada uno crea la pestaña ARBA y los paneles IA / Acero /
Metrados / Encofrado si no existen (en ese orden) y añade su botón al desplegable "Acero" del
panel "Acero", así que da igual cuál cargue primero y basta con instalar los archivos. Si se
desinstala Zapatas, solo hay que borrar `FootingRebar.addin` y la carpeta `FootingRebar\`; el
desplegable sigue con los demás botones.

## Parámetros compartidos del contrato ARBA (nada que instalar)

Al pulsar **Armar** por primera vez en un proyecto, el add-in crea (o completa) los parámetros
compartidos de ejemplar `ARBA - Origen`, `ARBA - Código` y `Metrado - Elemento` (GUID fijos del
contrato, grupo Datos) usando un archivo temporal que borra al terminar: **no** cambia el archivo
de parámetros compartidos del usuario (Gestionar > Parámetros compartidos sigue apuntando al suyo)
ni deja nada en `%TEMP%`. El instalador no tiene que copiar ningún archivo de parámetros.

## Comprobación tras instalar

1. Abrir Revit 2027.2 y aceptar la carga del add-in (si pide confirmación por el VendorId).
2. En la pestaña ARBA, panel Acero, desplegable Acero debe estar **Zapatas** con su icono
   (sección de zapata con la columna encima y la parrilla con ganchos). También aparece en
   Complementos > Herramientas externas > "Armar zapata".
3. Seleccionar una zapata estructural de hormigón y pulsar Zapatas: se abre la ventana
   "Armar zapatas" con el tema oscuro de Revit y, en el pie, "Contrato ARBA 1.0.0".
4. Al armar una zapata con marca `Z1`, los conjuntos creados llevan Partición
   `CIMIENTOS - ZAP-Z1`, `ARBA - Origen = ZAPATAS`, `ARBA - Código` = capa y
   `Metrado - Elemento = CIMIENTOS`. Al armarla otra vez pregunta si borrar la armadura del
   add-in y rearmar (no duplica) o conservar; con barras de la versión anterior (`ZAP-Z1`)
   ofrece migrarlas al contrato sin rearmar.

## Desinstalación

Borrar `%AppData%\Autodesk\Revit\Addins\2027\FootingRebar.addin` y la carpeta
`%AppData%\Autodesk\Revit\Addins\2027\FootingRebar\`.
