# Notas para ARBA-comun (desde Acero-Zapatas)

Lo que se echó en falta o convendría cambiar en el código común al integrar `v1.0.0` en este add-in.
Nada de esto se ha tocado en `external/ARBA-comun`; se resuelve aquí con lo mínimo y se anota para la
siguiente versión del común.

## 1. `Arba.Comun.props` no excluye el submódulo de los globs por defecto del SDK

El SDK incluye por defecto `**/*.cs` bajo el proyecto, así que en un add-in con el submódulo en
`external/ARBA-comun` entran también `tests/Program.cs` y `build/CheckUsage.cs` del común (los de `src/`
no se duplican porque csc deduplica rutas iguales). Compila, pero mete código ajeno en la DLL del add-in.
`Arba.Comun.Check.csproj` lo evita con `EnableDefaultCompileItems=false`, que un add-in no puede usar.

- **Aquí**: `FootingRebar.csproj` lleva, **antes** del `Import`,
  `<Compile Remove="external\**" />` y `<None Remove="external\**" />`.
- **Propuesta**: que el `.props` haga `<Compile Remove="$(ArbaComunDir)tests/**;$(ArbaComunDir)build/**" />`
  (o que `INTEGRACION.md` §2 documente el `Remove` y su posición respecto al `Import`).

## 2. `ArbaMigration.MigrateHost` asegura los ocho parámetros del contrato

`Run` llama a `ArbaSharedParams.EnsureAll`, así que un add-in que solo necesita `Origen`, `Codigo` y
`Elemento` deja vinculados también `ARBA - Anfitrión`, `Metrado - Partida/Material/Peso/Pernos` en cuanto
el usuario migra una zapata. No es un error (el plugin de metrados los crea igual), pero es más de lo que
`INTEGRACION.md` §5 dice que escribe un add-in de armado. Propuesta: parámetro opcional
`IEnumerable<ArbaParam> ensure` (por defecto todos) o asegurar solo los que la migración escribe
(`Origen`, `Codigo`, `Elemento`).

## 3. `ArbaOrigin.Find` depende de que exista el parámetro `ARBA - Origen`

Devuelve vacío si `ArbaSharedParams.IdOf` es null, lo cual es correcto, pero conviene decir en
`INTEGRACION.md` §6 que la comprobación de "barras propias" debe ir **después** de
`ArbaSharedParams.Ensure` + `Regenerate` (aquí se hace así, dentro de la transacción).

## 4. Referencia al código de Bloques en el prompt

`PROMPTS/02-Acero-Zapatas.md` §7 remite a `Fosa_transformadores/ArmarBloqueCommand.cs §3c` para el
`TaskDialog` de borrar/conservar; ese archivo no está en este repo ni en el común, así que el diálogo se
ha escrito a partir de la descripción (tres `CommandLink` + Cancelar). Si se quiere idéntico en todos los
add-ins, candidato a entrar en el común como `ArbaDialogs.AskExisting(ownCount, legacyCount)`.

## 5. Decisión local: plantilla vacía

`AppConfig.Normalize` sustituye una plantilla vacía por la del add-in (`{categoria} - {prefijo}-{marca}`,
sin `{codigo}`, como manda el contrato para Zapatas) y no por `ArbaContract.PartitionTemplate` (que lleva
`-{codigo}` y daría `CIMIENTOS - ZAP-Z1-inferior`). Si se prefiere lo contrario, es una constante.
