# TECH DEBT

| ID | Долг | Где | Почему допущен | План | Статус |
|---|---|---|---|---|---|
| DEBT-0001 | `dotnet` не в PATH в неинтерактивных оболочках (Claude Code Bash) — `~/.bashrc` их не затрагивает | окружение | .NET установлен в домашнюю папку без root | Добавить `export PATH="$HOME/.dotnet:$PATH"` в `~/.profile`; до тех пор — `$HOME/.dotnet/dotnet` | открыт |
