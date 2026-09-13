# Сторонние компоненты

Music Scan Integrity распространяется по лицензии [BSD 3-Clause](LICENSE.txt).
Эта лицензия покрывает только код самой программы. Перечисленные ниже
компоненты входят в готовые сборки и распространяются их правообладателями
на своих условиях.

## BASS, (c) un4seen developments

Декодирование аудио: `bass.dll` и плагины к ней в подпапке `bass\` рядом
с программой.

**Лицензией BSD 3-Clause не покрыта.** BASS бесплатна только для
некоммерческого использования; для коммерческого нужна лицензия
un4seen developments. Действующие условия — на [www.un4seen.com](https://www.un4seen.com/).

Это относится и к готовым сборкам из раздела Releases: сама программа
свободна, вложенные в неё библиотеки BASS — нет.

## Остальные компоненты

| Компонент | Назначение | Лицензия |
|---|---|---|
| [ManagedBass](https://github.com/ManagedBass/ManagedBass) | Обёртка над BASS для .NET | MIT |
| [TagLib#](https://github.com/mono/taglib-sharp) | Чтение тегов | LGPL-2.1 |
| [WPF-UI](https://github.com/lepoco/wpfui) | Оформление в стиле Fluent | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MVVM | MIT |
| [.NET](https://github.com/dotnet/runtime) | Среда выполнения, вложена в сборку | MIT |

Тексты лицензий MIT и LGPL-2.1 — в репозиториях соответствующих проектов.
