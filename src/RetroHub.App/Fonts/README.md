# Fontes

O tema procura, nesta ordem:

- **Interface:** `Inter` → `Geist` → Segoe UI Variable → Segoe UI
- **Mono:** `JetBrains Mono` → `Geist Mono` → Cascadia Mono → Consolas

Para embutir as fontes no executável, copie os `.ttf` para esta pasta
(ex.: `Inter-Regular.ttf`, `Inter-Medium.ttf`, `Inter-SemiBold.ttf`, `JetBrainsMono-Regular.ttf`).
Elas são incluídas como `Resource` pelo `.csproj` e referenciadas em `Themes/Tokens.xaml`.
Ambas são licenciadas sob a SIL Open Font License.

Sem os arquivos, o app usa as fontes do sistema (se Inter/JetBrains Mono estiverem instaladas no Windows, são usadas).
