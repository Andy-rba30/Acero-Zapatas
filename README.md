# Armado automático de zapatas — add-in Revit 2027

Genera la armadura de **zapatas** (aisladas, combinadas, corridas y losas de cimentación) a
partir de la **geometría real del elemento**, sin depender de los nombres de parámetros de tus
familias:

- **Parrilla inferior** en las dos direcciones: barras principales (a lo largo de `u`, la capa
  más baja) y secundarias (a lo largo de `v`, apoyadas encima), cada una con su tipo de barra,
  separación y **gancho estándar** en los dos extremos (dobla hacia arriba).
- **Parrilla superior** opcional (zapatas combinadas, conectadas o de gran canto), con sus dos
  capas y ganchos hacia abajo.
- **Columnas** que apoyan sobre la zapata, detectadas y dibujadas en los esquemas como
  referencia (su armado y arranques los pone el add-in de columnas).

Es el hermano del add-in de losas
([Acero-losas](https://github.com/Andy-rba30/Acero-losas)), del de columnas
([Acero-columnas](https://github.com/Andy-rba30/Acero-columnas)) y del de muros
([Acero-automatico](https://github.com/Andy-rba30/Acero-automatico)): misma base (lectura del
sólido, ventana previa con esquemas y tema oscuro de Revit, red de seguridad que deshace el
elemento entero si una barra queda fuera del hormigón, `config.json`) y el mismo botón en la
pestaña **ARBA** > panel **Acero** > desplegable **Acero** > **Zapatas**.

Antes de crear nada abre una **ventana** en la que se ve qué se ha detectado en cada zapata
seleccionada y se elige el armado: dirección de las barras principales (general y por zapata),
tipos de barra, separaciones, ganchos y recubrimientos, con un esquema en **planta** y de la
**sección transversal** (con la columna encima, como en el plano de detalle).

## Cómo lee la zapata

1. Toma los sólidos del elemento (cimentación estructural: instancia de familia de zapata,
   losa de cimentación o zapata corrida). Puede tener **varios sólidos** (zapata escalonada
   modelada en dos cuerpos); los despreciables (menos del 1 % del mayor) se ignoran.
2. El **canto** va de la cara inferior horizontal más baja a la cara superior horizontal más
   alta. Sin cara inferior horizontal (base inclinada) se rechaza con un mensaje claro y sin
   crear ninguna barra.
3. El **contorno inferior** sale de las caras a la cota inferior (bordes curvos teselados;
   puede tener huecos, por ejemplo un pase). El **contorno superior** sale de las caras a la
   cota superior: en una zapata **escalonada o piramidal** es menor y se marca como tal.
4. Busca las **columnas** (estructurales o arquitectónicas) cuya base cae sobre la zapata, para
   dibujarlas en los esquemas y contarlas en el informe.
5. Elige los **ejes locales**: `u` es la dirección de las barras principales, `v` la
   perpendicular. Por defecto `u` va paralela al **lado largo** de la zapata (las barras del
   lado largo van abajo, con más peralte útil); también puede ir por el lado corto, por los
   ejes X o Y del proyecto o con un ángulo. Se cambia en general y **zapata a zapata** desde la
   lista de la ventana.

## Reglas de armado (`FootingPlan`)

Cada barra es una recta `v = cte` (a lo largo de `u`) o `u = cte` (a lo largo de `v`),
**recortada contra el contorno con sus huecos**: cada tramo interior es una barra. Vale para
cualquier forma (rectangular, en L o T, con huecos, irregular). Además la franja de ancho
`recubrimiento lateral + diámetro` alrededor de la recta tiene que quedar dentro del hormigón,
así la barra guarda el recubrimiento con los bordes paralelos a ella.

- La **separación es un máximo**: `n = techo(L / s)` huecos iguales, con barra en los dos
  extremos al recubrimiento lateral. Las barras iguales y equiespaciadas se crean como **un
  solo conjunto de Revit (array)**, igual que si se modelaran a mano: cada capa de una zapata
  rectangular es un conjunto.
- **Parrilla inferior**: principal a `recubrimiento inferior + medio diámetro` de la cara
  inferior; secundaria apoyada encima (`+ diámetro principal`), repartidas en el contorno
  inferior.
- **Parrilla superior** (opcional): principal a `recubrimiento superior + medio diámetro` de la
  cara superior; secundaria colgada debajo, repartidas en el contorno superior (la plataforma
  en una zapata escalonada). Si las dos parrillas se solapan (canto pequeño) se rechaza.
- **Ganchos**: en los extremos que dan al borde exterior; en los bordes de un hueco la barra
  va recta. Con gancho, el tramo recto se **retranquea el radio exterior del doblez**
  (`diámetro de doblado del gancho / 2 + diámetro`, del tipo de barra) para que la cara exterior
  del gancho guarde el recubrimiento lateral. El plugin crea la primera barra de cada capa, lee
  hacia dónde dobla el gancho y si es al revés (abajo en la inferior, arriba en la superior) la
  borra y la vuelve a crear con la otra orientación.
- Los tramos más cortos que `minBarLengthMm` (300 mm) se omiten y se cuentan en el aviso.

## Comprobaciones de seguridad

Igual que en losas, columnas y muros: **o se arma la zapata entera y bien, o no se arma**.

1. **Antes de crear cada conjunto** se comprueba que el eje de la barra y cuatro fibras
   desplazadas medio diámetro (en horizontal perpendicular y en vertical) quedan dentro del
   hormigón (`Solid.IntersectWithCurve`) en todas las posiciones del array.
2. **Después de crear y regenerar** se lee la geometría real de cada barra de cada conjunto
   (**ganchos y radios de doblado incluidos**) y se vuelve a comprobar entera: un gancho que
   asome por la cara superior de una zapata delgada hace que se rechace el elemento.
3. Cualquier fallo deshace la subtransacción de ese elemento: no queda ni una barra.

El informe final dice, zapata a zapata, qué se ha creado (barras por capa y conjuntos) y por
qué se ha rechazado lo que no.

## Interfaz gráfica

- **Zapatas seleccionadas**: dimensiones `u × v`, dirección, canto, escalones, huecos,
  columnas encima y el resumen del armado (o, en rojo, el motivo del rechazo). Cada fila
  armable tiene su **dirección** propia. Clic en una fila para verla en los esquemas.
- **Dirección de las barras principales**: lado largo (por defecto), lado corto, X, Y o ángulo.
- **Parrilla inferior**: principal y secundaria (tipo de barra, separación, gancho).
- **Parrilla superior**: activar, principal y secundaria.
- **Recubrimientos, columnas y partición**: recubrimiento inferior (contra el terreno, 75 mm
  por defecto), superior (50) y lateral (75); mostrar las columnas; plantilla del parámetro
  Partición (`{marca}`, `{id}`, `{tipo}`, `{familia}`, `{conjunto}`, `{capa}`).
- **Planta**: hormigón de la cara inferior con huecos, cara superior a trazos si es menor,
  columnas (gris), cada barra a su grosor y con el color de su capa (rojo oscuro inferior
  principal, morado inferior secundaria, naranja superior principal, azul superior secundaria)
  y marcas de gancho. Rueda: zoom; arrastrar: mover; doble clic: encajar. Al pasar el ratón
  por una barra se ve su capa, diámetro, posición y longitud.
- **Sección transversal** a media luz: el **perfil real** del hormigón (muestreado del
  sólido, con escalones y taludes), el terreno, la columna que apoya encima, cada barra
  principal como un círculo a su diámetro y la secundaria más cercana al corte como una raya
  con sus ganchos (hacia arriba abajo, hacia abajo arriba).
- **Guardar como valores por defecto** escribe `config.json`; **Armar** crea las barras;
  **Cancelar** no toca nada.

Sin tipo de barra elegido los esquemas se dibujan con diámetros orientativos (12.7 mm) y el
botón Armar avisa de qué falta.

## Limitaciones

- Solo zapatas de **base plana horizontal**. Las escalonadas o piramidales se arman en la
  cara inferior (parrilla inferior) y en la plataforma superior (parrilla superior); los
  taludes no llevan armadura propia.
- Las barras se reparten **uniformemente**. No se concentra la armadura corta en la banda
  central de las zapatas rectangulares (ACI 318 13.3.3.3 / E.060) ni se calculan cuantías: la
  separación la da el usuario.
- No se colocan los **arranques de columna** (barras longitudinales con gancho de 90° sobre
  la parrilla y estribos de arranque, el "1@50, 5@100" del plano): eso es armado de la
  columna y lo hace el add-in de columnas. Las columnas solo se dibujan como referencia.
- No se colocan estribos ni barras en U en los bordes de la zapata.
- No se comprueban choques entre barras de capas distintas más allá del apilado de cotas, ni
  con los arranques de la columna.

## config.json

```jsonc
{
  "coverBottomMm": 75, "coverTopMm": 50, "coverEdgeMm": 75,
  "direction": { "mode": "long", "angleDeg": 0 },   // long | short | x | y | angle
  "bottom": {
    "main":      { "barTypeName": "", "spacingMm": 200, "hookTypeName": "" },
    "secondary": { "enabled": true, "barTypeName": "", "spacingMm": 200, "hookTypeName": "" }
  },
  "top": {
    "enabled": false,
    "main":      { "barTypeName": "", "spacingMm": 200, "hookTypeName": "" },
    "secondary": { "enabled": true, "barTypeName": "", "spacingMm": 200, "hookTypeName": "" }
  },
  "detectColumns": true,
  "partitionTemplate": "ZAP-{marca}",
  "toleranceMm": 2, "minBarLengthMm": 300
}
```

Los nombres de tipo de barra y de gancho pueden ser exactos o un fragmento (`"1/2"`,
`"90"`); sin coincidencia no se arma, nunca se sustituye por otro tipo.

## Compilar e instalar

Requiere el SDK de .NET 10 y Revit 2027.2 (los paquetes `Nice3point.Revit.Api.*` 2027.2
traen las DLL de la API; para Revit 2025/2026 cambia el `TargetFramework` a
`net8.0-windows` y la versión del paquete).

```
dotnet build -c Debug
```

En Debug la compilación copia `FootingRebar.dll`, `config.json` y `FootingRebar.addin` a
`%AppData%\Autodesk\Revit\Addins\2027\`. Al abrir Revit aparece la pestaña **ARBA** con el
panel **Acero** y el botón **Zapatas** dentro del desplegable **Acero** (comparte la pestaña y
el desplegable con los add-ins de columnas, muros y losas si están instalados) y el comando
queda también en Complementos > Herramientas externas.

El proyecto lleva `EnableWindowsTargeting`, así que también compila en Linux o macOS para
comprobar el código (la DLL solo sirve en Windows con Revit). Las clases puras se prueban sin
Revit con el programa de consola de `Tests/`:

```
cd Tests && dotnet run
```

## Estructura del código

| Archivo | Qué hace |
|---------|----------|
| `Geometry2D.cs` | Geometría pura: contorno con huecos, recorte scan-line de una recta con franja de recubrimiento, reparto de posiciones, unión de tramos. |
| `FootingPlan.cs` | Armado de la zapata (parrillas inferior y superior, ganchos con retranqueo, conjuntos). Pura, compartida por ventana y generador. |
| `FootingOutline.cs` | Lectura del sólido de Revit: caras inferior y superior, contornos, columnas encima, perfil de la sección; sistema local por dirección (`FootingFrame`). |
| `HostAnalysis.cs` | Resultado por elemento (contorno o motivo de rechazo) y dirección propia. |
| `RebarGenerator.cs` | Crea los `Rebar` con las dos redes de seguridad y la orientación automática de ganchos. |
| `RebarOptionsWindow.cs`, `PlanPreview.cs`, `SectionPreview.cs` | Ventana y esquemas (WPF en código, sin XAML). |
| `RevitTheme.cs` | Tema oscuro al estilo de Revit 2027 (el mismo que en columnas y losas). |
| `ArmarZapataCommand.cs`, `RibbonApp.cs` | Comando externo y pestaña de la cinta. |
| `AppConfig.cs`, `PartitionName.cs` | Configuración y plantilla de Partición. |
| `Tests/` | Pruebas de consola de las clases puras. |
| `PLAN.md` | Plan de trabajo y estado del proyecto. |
| `INSTALADOR.md` | Instrucciones para añadir el plugin al instalador de ARBA. |

Las clases puras (`Geometry2D`, `FootingPlan`, `AppConfig`, `PartitionName`) no dependen de
Revit y se prueban en el programa de consola.
