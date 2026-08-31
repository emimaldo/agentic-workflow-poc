# Contributing

Gracias por querer contribuir. Estas pautas ayudan a mantener el repositorio ordenado y los PRs fáciles de revisar.

Branching
- Trabajar desde branches temáticos: `feature/<descriptivo>`, `fix/<descriptivo>`, `chore/<descriptivo>`.

Commits
- Mensajes en inglés o español breve y claros. Formato recomendado:
  - `feat: Descripción corta` — nuevas funcionalidades
  - `fix: Descripción corta` — correcciones
  - `test: Añade pruebas para ...`

Pull Request checklist
- [ ] Incluye descripción clara del cambio y motivo
- [ ] Todos los tests pasan localmente (`dotnet test`)
- [ ] Añadí/actualicé tests para código nuevo o bugs corregidos
- [ ] Añadí notas de migración o breaking changes si aplica

Estilo y pruebas
- Mantener `Nullable` habilitado (el repo usa `Nullable: enable`).
- Escribir tests unitarios para comportamiento crítico y tests E2E para flujos importants.
- Evitar cambios en proyectos no relacionados en el mismo PR.

Revisión
- Etiquetar reviewers relevantes y explicar el alcance del PR.

Configuración local recomendada
- .NET 9 SDK
- Ejecutar `dotnet restore` y `dotnet test` antes de push

Contacto
- Abrir un issue si necesitás discutir una gran reescritura o diseño.
