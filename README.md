# Understander

Мобильное приложение-переводчик на .NET MAUI с основным сценарием использования на Android.
Проект объединяет перевод текста, распознавание речи, перевод диалога, OCR по фотографии,
озвучивание результата и локальную историю переводов.

> **Актуальность документации:** 24 ноября 2026 года
> **Текущее состояние:** основная функциональность реализована; проект находится в стадии
> интеграционного тестирования на Android-эмуляторе и физических устройствах.

![Platform](https://img.shields.io/badge/platform-Android-3DDC84?style=flat-square&logo=android&logoColor=white)
![Framework](https://img.shields.io/badge/.NET%20MAUI-9-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![Language](https://img.shields.io/badge/language-C%23%2013-239120?style=flat-square&logo=csharp&logoColor=white)
![Database](https://img.shields.io/badge/database-SQLite-003B57?style=flat-square&logo=sqlite&logoColor=white)
![Status](https://img.shields.io/badge/status-Active%20development-yellow?style=flat-square)

---

## Содержание

- [Назначение приложения](#назначение-приложения)
- [Возможности](#возможности)
- [Поддерживаемые языки](#поддерживаемые-языки)
- [Как работает распознавание речи](#как-работает-распознавание-речи)
- [Архитектура](#архитектура)
- [Структура проекта](#структура-проекта)
- [Технологический стек](#технологический-стек)
- [Внешние сервисы и ограничения](#внешние-сервисы-и-ограничения)
- [Требования к окружению](#требования-к-окружению)
- [Запуск из Visual Studio](#запуск-из-visual-studio)
- [Сборка и запуск из PowerShell](#сборка-и-запуск-из-powershell)
- [Сценарии использования](#сценарии-использования)
- [Разрешения Android](#разрешения-android)
- [Диагностика](#диагностика)
- [Известные ограничения](#известные-ограничения)
- [Текущее состояние функциональности](#текущее-состояние-функциональности)
- [Команда](#команда)

---

## Назначение приложения

**Understander** предназначен для быстрого перевода текста и коротких реплик между
собеседниками. Приложение рассчитано прежде всего на Android и использует стандартные
системные возможности устройства, когда это возможно:

- системное распознавание речи Google через `RecognizerIntent`;
- системный Text-to-Speech для озвучивания перевода;
- камеру или галерею для получения изображения;
- локальную SQLite-базу для истории.

Архитектура построена на MVVM. Пользовательский интерфейс реализован на XAML, сервисы
перевода, OCR, речи и базы данных отделены от экранов и ViewModel.

---

## Возможности

### Главный экран

- ввод текста вручную;
- ввод текста на русском и других языках через системную клавиатуру Android;
- вставка текста из буфера обмена;
- выбор исходного и целевого языка;
- обмен языков местами;
- перевод через выбранный движок;
- автоматический переход к другим зарегистрированным переводчикам при ошибке;
- копирование результата;
- озвучивание результата через Android Text-to-Speech;
- перевод текста с фотографии.

### Режим диалога

Режим диалога предназначен для общения двух людей:

- задаются два языка собеседников;
- приложение по очереди ожидает реплику активного собеседника;
- текст распознаётся системным Google Speech UI;
- распознанная реплика добавляется в список сообщений;
- для неё выполняется перевод на язык второго собеседника;
- после успешной реплики активный собеседник переключается автоматически;
- языки можно поменять местами кнопкой `⇄`.

**Важно:** в актуальной реализации распознавание запускается системным экраном Google.
После нажатия микрофона нужно говорить и завершить запись средствами этого системного
экрана. Это не режим удержания кнопки Understander и не схема «нажал/отпустил».

### История

- записи переводов сохраняются локально в SQLite;
- история доступна без повторного запроса к API;
- записи можно открыть повторно;
- поддерживается удаление записей.

### OCR

Распознавание текста по фотографии использует два Android-механизма:

- `Plugin.Maui.OCR` / ML Kit для поддерживаемых письменностей;
- Tesseract4Android для русского текста и кириллицы.

Модели Tesseract находятся в `Resources/Raw/tessdata`: `eng.traineddata` и `rus.traineddata`.

---

## Поддерживаемые языки

| Код | Язык |
|---|---|
| `ru` | Русский |
| `en` | English |
| `de` | Deutsch |
| `fr` | Français |
| `es` | Español |
| `it` | Italiano |
| `tr` | Türkçe |
| `zh` | 中文 |

Для Android Speech Intent языковые коды преобразуются в локали, например `ru` → `ru-RU`
и `en` → `en-US`. Фактическая доступность языка зависит от установленных Google Speech
Services и языковых пакетов устройства.

---

## Как работает распознавание речи

Текущая Android-цепочка использует системный `RecognizerIntent`, а не ручную запись PCM:

```text
DialogueViewModel
    ↓
AndroidSpeechRecognizer.StartListeningAsync(language)
    ↓
RecognizerIntent.ActionRecognizeSpeech
    ↓
системный Google Speech Services
    ↓
MainActivity.OnActivityResult
    ↓
AndroidSpeechRecognizer.CompleteFromActivity
    ↓
DialogueViewModel
```

Этот подход выбран потому, что системное приложение Google на эмуляторе и телефоне уже
подтверждает работоспособность такого сценария. Understander передаёт локаль, получает
`RecognizerIntent.ExtraResults` и использует первый непустой результат.

Для диагностики используется тег logcat:

```text
Understander.Speech
```

Ожидаемые сообщения:

```text
Launching Google RecognizerIntent: ru-RU
Google RecognizerIntent result: 'привет'
```

---

## Архитектура

Проект использует MVVM и разделён на следующие уровни.

### Views

- `MainPage` — основной переводчик;
- `DialoguePage` — режим диалога;
- `HistoryPage` — история переводов.

### ViewModels

- `MainViewModel` — текстовый перевод, OCR, буфер и история;
- `DialogueViewModel` — языки собеседников, речь и сообщения;
- `HistoryViewModel` — просмотр истории;
- `AsyncCommand` — адаптер асинхронных операций для XAML.

### Services

- `ITranslator` — общий интерфейс переводчика;
- `GoogleTranslator` — Google Translate endpoint;
- `MyMemoryTranslator` — MyMemory Translation API;
- `LaraTranslator` — Lara SDK;
- `ISpeechRecognizer` — интерфейс распознавания речи;
- `AndroidSpeechRecognizer` — Android-реализация через Google `RecognizerIntent`;
- `NoOpSpeechRecognizer` — заглушка для платформ без Android-реализации;
- `OcrService` — OCR-сервис;
- `HistoryDatabase` — SQLite-хранилище истории.

Регистрация зависимостей выполняется в `MauiProgram.cs`. На Android используется
`AndroidSpeechRecognizer`, на остальных платформах — `NoOpSpeechRecognizer`.

---

## Структура проекта

```text
Understander/
├── Converters/                         # Конвертеры для XAML
├── Models/                             # Модели данных
├── Platforms/
│   ├── Android/
│   │   ├── AndroidManifest.xml         # Android-разрешения и queries
│   │   ├── AndroidSpeechRecognizer.cs  # Google RecognizerIntent
│   │   ├── MainActivity.cs             # Получение результата Intent
│   │   ├── MainApplication.cs          # Android entry point
│   │   └── TesseractOcr.cs             # Android OCR
│   ├── iOS/                            # iOS-конфигурация
│   ├── MacCatalyst/                    # Mac Catalyst-конфигурация
│   ├── Tizen/                          # заготовка Tizen
│   └── Windows/                        # Windows-конфигурация
├── Resources/                          # шрифты, изображения, стили, tessdata
├── Services/                           # перевод, OCR, речь, база
├── ViewModels/                         # MVVM-логика
├── Views/                              # XAML-экраны
├── App.xaml                            # ресурсы приложения
├── AppShell.xaml                       # TabBar и маршруты
├── MauiProgram.cs                      # DI и запуск MAUI
├── Understander.csproj                 # проект и NuGet-пакеты
└── Understander.sln                    # solution Visual Studio
```

---

## Технологический стек

- C# 13;
- .NET 9 и .NET MAUI 9;
- Android API 21+ как минимальная версия проекта;
- Visual Studio 2022 с workload **.NET Multi-platform App UI development**;
- `CommunityToolkit.Maui`;
- `CommunityToolkit.Mvvm`;
- `sqlite-net-pcl` 1.9.172;
- `Plugin.Maui.OCR` 1.1.1;
- `Plugin.Maui.Audio` 3.0.0;
- `Lara.Sdk` 1.11.2;
- Tesseract4Android 4.9.0 (`libs/tesseract4android-4.9.0.aar`).

Целевые framework проекта:

```text
net9.0-android
net9.0-ios
net9.0-maccatalyst
net9.0-windows10.0.19041.0  # добавляется на Windows-хосте
```

Основное тестирование выполняется на Android.

---

## Внешние сервисы и ограничения

В проекте зарегистрированы три переводчика:

1. Google — выбранный пользователем провайдер может быть вызван первым;
2. MyMemory — запасной REST API;
3. Lara — SDK-провайдер с ключами доступа.

В главном экране используется именно выбранный пользователем провайдер. Автоматическая
подмена Google на MyMemory отключена, чтобы результат и ошибка соответствовали выбранному
движку. Другой провайдер можно выбрать вручную в поле «Движок».

`GoogleTranslator` использует публичный endpoint `translate.googleapis.com` с параметром
`client=gtx`. Это не официальный Google Cloud Translation API. Google может ограничивать
такие запросы, поэтому для production рекомендуется официальный API с серверной защитой
ключа.

`LaraTranslator` читает переменные окружения:

```text
LARA_ACCESS_KEY_ID
LARA_ACCESS_KEY_SECRET
```

Перед публикацией необходимо удалить секреты из исходников, заменить ключи и хранить их
вне репозитория.

Lara остаётся доступен в списке движков даже без credentials, но при выборе без настроенных
переменных окружения приложение показывает понятную ошибку вместо падения при запуске.

Текстовый перевод и Lara/MyMemory требуют интернет-соединения. SQLite, история и локальные
Tesseract-модели работают без сети. Распознавание зависит от Google Speech Services и может
использовать сетевой fallback.

---

## Требования к окружению

1. Windows 10/11;
2. Visual Studio 2022 17.12 или новее;
3. workload **.NET Multi-platform App UI development**;
4. .NET SDK 9;
5. Android SDK и Platform Tools;
6. Android Emulator с образом Google APIs или физический Android-телефон;
7. отладка по USB для физического устройства;
8. Google Speech Services и нужные языковые пакеты на Android.

Проверка окружения:

```powershell
dotnet --info
adb devices
```

---

## Запуск из Visual Studio

1. Откройте файл `Understander.sln` из корня репозитория.
2. Дождитесь восстановления NuGet-пакетов.
3. Выберите Android Emulator или подключённый телефон.
4. Выберите конфигурацию `Debug`.
5. Нажмите `F5` или **Start**.
6. При первом запуске разрешите доступ к микрофону и камере.

После существенных изменений рекомендуется удалить старую APK и установить сборку заново.

---

## Сборка и запуск из PowerShell

```powershell
Set-Location .
dotnet restore .\Understander.csproj
dotnet clean .\Understander.csproj
dotnet build .\Understander.csproj -f net9.0-android -c Debug
dotnet build .\Understander.csproj -t:Run -f net9.0-android -c Debug
```

Для чистого теста можно удалить приложение:

```powershell
adb uninstall <application-id>
```

---

## Сценарии использования

### Перевод текста

1. Откройте **Главная**.
2. Выберите исходный и целевой язык.
3. Выберите движок.
4. Введите текст или нажмите **Вставить**.
5. Нажмите **Перевести →**.
6. При ошибке выбранного сервиса будет выполнен fallback к другим провайдерам.

### Речь в режиме диалога

1. Откройте **Режим диалога**.
2. Проверьте языки двух собеседников.
3. Нажмите микрофон.
4. В системном Google-окне разрешите микрофон.
5. Произнесите реплику.
6. Завершите запись в системном окне Google.
7. Дождитесь оригинала и перевода в карточке сообщения.

### OCR-перевод

1. На главном экране нажмите кнопку камеры.
2. Выберите камеру или галерею.
3. Разрешите доступ к камере/фотографиям.
4. Дождитесь распознавания текста.
5. Проверьте поле ввода и запустите перевод.

### Озвучивание

1. Выполните перевод.
2. Нажмите кнопку динамика.
3. Приложение выберет системную локаль Text-to-Speech для языка результата.

---

## Разрешения Android

В `Platforms/Android/AndroidManifest.xml` заявлены:

- `INTERNET` — сетевые API;
- `ACCESS_NETWORK_STATE` — состояние сети;
- `CAMERA` — фотографирование для OCR;
- `RECORD_AUDIO` — речь;
- `MODIFY_AUDIO_SETTINGS` — аудиоподсистема.

Доступ к микрофону дополнительно запрашивается во время работы через
`Permissions.Microphone`. Для Android 11+ в manifest добавлен `queries` с
`android.speech.RecognitionService`, чтобы система находила сервис распознавания.

---

## Диагностика

Основной тег logcat:

```text
Understander.Speech
```

Команды:

```powershell
adb logcat -c
adb logcat -v time | Select-String `
  "Understander.Speech|RecognizerIntent|RecognitionService|AudioRecord|Google"
```

Ожидаемый результат:

```text
Launching Google RecognizerIntent: ru-RU
Google RecognizerIntent result: 'привет'
```

Если системное окно не появляется, проверьте Google Speech Services, голосовой ввод,
разрешение `RECORD_AUDIO`, установленную APK и наличие `RecognitionService`.

Проверка разрешений и пакетов:

```powershell
adb shell dumpsys package <application-id>
adb shell pm list packages | Select-String "google|speech|tts"
```

Сообщение `Google Translate заблокировал запрос` относится к текстовому HTTP-переводу,
а не к микрофону и Google Speech Intent.

---

## Известные ограничения

- Основной и наиболее проверенный сценарий — Android.
- На других платформах регистрируется `NoOpSpeechRecognizer`.
- Речь зависит от Google Speech Services и языковых пакетов устройства.
- Публичный Google Translate endpoint может блокировать запросы.
- Бесплатные сервисы перевода имеют ограничения по частоте и объёму.
- Lara-ключи нужно вынести из исходного кода перед публикацией.
- Качество OCR зависит от освещения, резкости, шрифта и языка.
- История хранится только локально и не синхронизируется с сервером.

---

## Текущее состояние функциональности

| Компонент | Состояние |
|---|---|
| Главный экран перевода | Реализован |
| Выбор и обмен языков | Реализован |
| Google Translator | Реализован; публичный endpoint может блокироваться |
| MyMemory fallback | Реализован |
| Lara Translator | Реализован; нужны рабочие credentials |
| Android Speech | Реализован через системный Google RecognizerIntent |
| Режим диалога | Реализован |
| OCR с камеры/галереи | Реализован |
| Tesseract-модели `rus`/`eng` | Подключены |
| Text-to-Speech | Реализован через MAUI API |
| SQLite-история | Реализована |
| Android-разрешения | Настроены |
| Unit-тесты | Не добавлены |
| CI/CD | Не настроен |

---

## Команда

Проект разрабатывается учебной командой. Зоны ответственности разделены между
архитектурой и backend-частью, пользовательским интерфейсом и интеграцией внешних API.

Проект разработан в учебных целях.

---

## Лицензии и сторонние сервисы

Перед распространением приложения необходимо проверить условия использования .NET MAUI,
NuGet-пакетов, Google Speech Services, Google Translate endpoint, MyMemory API, Lara SDK,
ML Kit, Tesseract4Android и языковых моделей. Отдельный файл лицензии проекта в текущей
версии отсутствует.