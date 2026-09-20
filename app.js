(function () {
    'use strict';

    var REPO = 'J-udgW05/MusicScan';


    var HTML_KEYS = [
        'footer_copy',
        'pp_s2_i1', 'pp_s2_i2', 'pp_s2_p2',
        'pp_s3_i1', 'pp_s3_i2', 'pp_s3_i3', 'pp_s3_p2',
        'pp_s5_p2', 'pp_s9_p1',
        'dn_s5_i1', 'dn_s5_i2'
    ];
    var root = document.documentElement;

    var T = {
        ru: {
            nav_check: 'Как это работает',
            nav_install: 'Скачать',
            nav_repo: 'Репозиторий',
            nav_donate: 'Поддержать',
            hero_title: 'Проверка музыкальной коллекции на повреждённые файлы',
            hero_lede: 'Отличает по-настоящему повреждённые файлы от тех, что просто без тегов или выглядят необычно.',
            hero_download: 'Скачать',
            hero_source: 'Исходный код',
            check_kicker: 'Как это работает',
            check_title: 'Две независимые проверки',
            check_lede: 'Декодер отвечает на вопрос «читается ли звук», разбор формата - на вопрос «сходится ли файл сам с собой». Второе ловит то, что первое пропускает: обрезанный FLAC декодируется до места разрыва без единой ошибки.',
            check_note: 'Отдельно можно включить слежение за порчей: программа запоминает отпечаток каждого файла и на следующей проверке замечает подмену содержимого при неизменных размере и дате - так выглядит сбойный диск.',
            m1_title: 'Декодер',
            m1_role: 'Библиотека BASS',
            m1_1: 'Обрывы и ошибки потока',
            m1_2: 'Пустые и нечитаемые файлы',
            m1_3: 'Таймауты чтения',
            m1_4: 'Занятые другими программами файлы',
            m1_5: 'Двенадцать библиотек декодирования',
            m2_title: 'Разбор формата',
            m2_role: 'Контейнер и контрольные суммы',
            m2_1: 'Контрольные суммы FLAC и Ogg',
            m2_2: 'Структура кадров MP3 и MP4',
            m2_3: 'Сверка заявленной длины',
            m2_4: 'Контейнеры RIFF, WavPack, APE',
            m2_5: 'Теги не путаются со звуком',
            fmt_kicker: 'Форматы',
            fmt_title: 'Что проверяется',
            fmt_audio: 'Аудио',
            fmt_tracker: 'Трекерные и MIDI',
            fmt_dsd: 'DSD',
            fmt_playlists: 'Плейлисты',
            inst_kicker: 'Установка',
            inst_title: 'Установщик или портативная версия',
            inst_req: 'Для Windows 10/11',
            inst_1_title: 'Установщик',
            inst_2_title: 'Портативная версия',
            inst_download: 'Скачать',
            inst_note: 'Сборка не подписана сертификатом: при первом запуске Windows покажет «Неизвестный издатель» → «Подробнее» → «Выполнить в любом случае».',
            footer_copy: '© 2026 Music Scan Integrity · <a href="https://github.com/J-udgW05" target="_blank" rel="noopener noreferrer">J-udgW05</a>',
            footer_docs: 'Документация',
            footer_license: 'Лицензия',
            footer_third: 'Компоненты',
            footer_privacy: 'Конфиденциальность',
            footer_email: 'j-udgw05@tuta.io',
            footer_copied: 'Скопировано',
            index_desc: 'Программа для Windows 10 и 11: проверяет музыкальную коллекцию на повреждённые файлы двумя независимыми способами — декодированием и разбором формата.',
            dn_doc_title: 'Поддержать проект — Music Scan Integrity',
            dn_desc: 'Music Scan Integrity бесплатна и с открытым кодом. На что идут пожертвования и как поддержать проект.',
            dn_title: 'Поддержать проект',
            dn_s4_p1: 'Если Music Scan Integrity оказалась вам полезна и вы хотите поддержать её развитие — вот как это можно сделать.',
            dn_s5_h: 'Помочь можно и бесплатно',
            dn_s5_p1: 'Деньги — не единственная и далеко не обязательная форма участия. Всё перечисленное ниже помогает проекту не меньше.',
            dn_s5_i1: 'Звезда <a href="https://github.com/J-udgW05/MusicScan">репозиторию</a> и рассказ о программе тем, кому она пригодится',
            dn_s5_i2: 'Сообщение об ошибке с примером файла <a href="https://github.com/J-udgW05/MusicScan/issues">в разделе Issues</a> — после самого кода это самая полезная помощь',
            dn_s5_i3: 'Перевод, документация, правки кода — pull request всегда к месту',
            pp_doc_title: 'Политика конфиденциальности — Music Scan Integrity',
            pp_desc: 'Что сайт Music Scan Integrity сохраняет в браузере и какие запросы отправляет при открытии страницы.',
            pp_title: 'Политика конфиденциальности',
            pp_date: 'Обновлено 15 сентября 2026 года',
            pp_s1_h: 'О чём эта страница',
            pp_s1_p1: 'Это страница проекта Music Scan Integrity — программы для проверки музыкальной коллекции на повреждённые файлы. Здесь нет ни регистрации, ни личного кабинета, ни форм обратной связи: весь сайт — это страница с описанием и ссылками на загрузку, страница о поддержке проекта и эта страница.',
            pp_s1_p2: 'Ниже перечислено ровно то, что происходит при открытии страницы. Список составлен по исходному коду сайта, который открыт и доступен для проверки.',
            pp_s2_h: 'Что сохраняется в вашем браузере',
            pp_s2_p1: 'Сайт сохраняет два значения в локальном хранилище браузера (localStorage):',
            pp_s2_i1: '<code>msi-theme</code> — выбранная вами тема: светлая или тёмная',
            pp_s2_i2: '<code>msi-lang</code> — выбранный язык страницы: русский или английский',
            pp_s2_p2: 'Эти записи нужны только для того, чтобы при следующем заходе страница открылась в том же виде. Они <b>хранятся в вашем браузере и никуда не отправляются</b> — ни на сервер сайта, ни автору, ни кому-либо ещё. Удалить их можно в настройках браузера вместе с данными сайта; страница после этого снова возьмёт тему и язык из настроек системы и браузера.',
            pp_s2_p3: 'Файлы cookie сайт не использует.',
            pp_s3_h: 'Какие запросы уходят при открытии страницы',
            pp_s3_p1: 'Сайт обращается к трём внешним адресам. При любом таком обращении браузер по устройству сети сообщает получателю IP-адрес, тип браузера и время запроса — это неизбежная часть работы интернета, а не сбор данных сайтом.',
            pp_s3_i1: '<b>GitHub Pages</b> — здесь сайт размещён, оттуда браузер получает саму страницу',
            pp_s3_i2: '<b>Google Fonts</b> (fonts.googleapis.com, fonts.gstatic.com) — оттуда загружаются шрифты страницы',
            pp_s3_i3: '<b>api.github.com</b> — один запрос за номером последней версии программы и именами файлов, чтобы кнопки «Скачать» вели прямо на них; уходит только с главной страницы',
            pp_s3_p2: 'Обработка данных на стороне GitHub и Google подчиняется их собственным условиям: <a href="https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement">заявление GitHub о конфиденциальности</a> и <a href="https://policies.google.com/privacy">политика конфиденциальности Google</a>. Автор сайта доступа к этим данным не имеет.',
            pp_s4_h: 'Чего сайт не делает',
            pp_s4_i1: 'Не устанавливает счётчики посещаемости и системы аналитики',
            pp_s4_i2: 'Не показывает рекламу и не подключает рекламные сети',
            pp_s4_i3: 'Не собирает имена, адреса электронной почты и другие личные сведения',
            pp_s4_i4: 'Не отслеживает вас между сайтами и не составляет профиль посетителя',
            pp_s4_i5: 'Не передаёт и не продаёт данные третьим лицам — передавать нечего',
            pp_s5_h: 'Сама программа',
            pp_s5_p1: 'Music Scan Integrity работает без подключения к сети и только читает файлы: ничего не удаляет, не переименовывает и никуда не отправляет. Настройки и база отпечатков для слежения за порчей остаются на вашем компьютере — рядом с программой либо в профиле пользователя, если рядом с программой писать нельзя.',
            pp_s5_p2: 'Проверить это можно по исходному коду: <a href="https://github.com/J-udgW05/MusicScan">репозиторий проекта</a> открыт.',
            pp_s6_h: 'Переходы на другие сайты',
            pp_s6_p1: 'Ссылки со страницы ведут на GitHub — к исходному коду, документации и файлам выпусков. После перехода действуют правила того сайта, на который вы попали.',
            pp_s7_h: 'Ваши права',
            pp_s7_p1: 'Сайт не хранит о вас никаких сведений на своей стороне, поэтому запрашивать, исправлять или удалять здесь нечего. Всё, что сайт создал, лежит в вашем браузере, и вы удаляете это сами, очистив данные сайта.',
            pp_s7_p2: 'По данным, которые получают GitHub и Google как владельцы серверов, обращаться следует к ним — порядок описан в их документах, ссылки выше.',
            pp_s8_h: 'Изменения',
            pp_s8_p1: 'Если состав запросов или сохраняемых значений изменится, изменится и эта страница, а дата вверху обновится. История правок видна в репозитории проекта — задним числом переписать её незаметно не получится.',
            pp_s9_h: 'Вопросы',
            pp_s9_p1: 'Вопросы по этой странице и по программе — <a href="https://github.com/J-udgW05/MusicScan/issues">в разделе Issues репозитория</a>.',
            nav_back: 'Вернуться на сайт',
            aria_theme_dark: 'Включить тёмную тему',
            aria_theme_light: 'Включить светлую тему',
            aria_back_top: 'Наверх',
        },
        en: {
            nav_check: 'How it works',
            nav_install: 'Download',
            nav_repo: 'Repository',
            nav_donate: 'Donate',
            hero_title: 'Checks a music collection for corrupted files',
            hero_lede: 'Tells truly damaged files apart from ones that merely lack tags or look unusual.',
            hero_download: 'Download',
            hero_source: 'Source code',
            check_kicker: 'How it works',
            check_title: 'Two independent checks',
            check_lede: 'The decoder answers whether audio can be read at all; format parsing answers whether the file agrees with itself. The second catches what the first misses: a truncated FLAC decodes right up to the break without a single error.',
            check_note: 'Corruption tracking can be turned on separately: the program remembers a fingerprint of every file and on the next check notices content swapped in place while size and date stay the same - the signature of a failing drive.',
            m1_title: 'Decoder',
            m1_role: 'BASS library',
            m1_1: 'Stream breaks and errors',
            m1_2: 'Empty and unreadable files',
            m1_3: 'Read timeouts',
            m1_4: 'Files locked by other programs',
            m1_5: 'Twelve decoding libraries',
            m2_title: 'Format parsing',
            m2_role: 'Container and checksums',
            m2_1: 'FLAC and Ogg checksums',
            m2_2: 'MP3 and MP4 frame structure',
            m2_3: 'Declared length verification',
            m2_4: 'RIFF, WavPack, APE containers',
            m2_5: 'Tags never mistaken for audio',
            fmt_kicker: 'Formats',
            fmt_title: 'What gets checked',
            fmt_audio: 'Audio',
            fmt_tracker: 'Tracker and MIDI',
            fmt_dsd: 'DSD',
            fmt_playlists: 'Playlists',
            inst_kicker: 'Installation',
            inst_title: 'Installer or portable build',
            inst_req: 'For Windows 10/11',
            inst_1_title: 'Installer',
            inst_2_title: 'Portable build',
            inst_download: 'Download',
            inst_note: 'The build is not code-signed: on first launch Windows shows “Unknown publisher” → “More info” → “Run anyway”.',
            footer_copy: '© 2026 Music Scan Integrity · <a href="https://github.com/J-udgW05" target="_blank" rel="noopener noreferrer">J-udgW05</a>',
            footer_docs: 'Documentation',
            footer_license: 'License',
            footer_third: 'Notices',
            footer_privacy: 'Privacy',
            footer_email: 'j-udgw05@tuta.io',
            footer_copied: 'Copied',
            index_desc: 'A Windows 10 and 11 program that checks a music collection for corrupted files two independent ways — by decoding and by parsing the format.',
            dn_doc_title: 'Support the project — Music Scan Integrity',
            dn_desc: 'Music Scan Integrity is free and open source. Where donations go and how to support the project.',
            dn_title: 'Support the project',
            dn_s4_p1: 'If Music Scan Integrity has been useful to you and you would like to support its development, here is how.',
            dn_s5_h: 'Helping without money',
            dn_s5_p1: 'Money is neither the only nor a required form of taking part. Everything below helps the project just as much.',
            dn_s5_i1: 'A star on the <a href="https://github.com/J-udgW05/MusicScan">repository</a>, and a word about the program to someone who would find it useful',
            dn_s5_i2: 'A bug report with a sample file <a href="https://github.com/J-udgW05/MusicScan/issues">in the Issues section</a> — after the code itself, this is the most useful help there is',
            dn_s5_i3: 'Translations, documentation, code fixes — a pull request is always welcome',
            pp_doc_title: 'Privacy policy — Music Scan Integrity',
            pp_desc: 'What the Music Scan Integrity site stores in your browser and what requests it makes when the page opens.',
            pp_title: 'Privacy policy',
            pp_date: 'Updated 15 September 2026',
            pp_s1_h: 'What this page is',
            pp_s1_p1: 'This is the project page for Music Scan Integrity — a program that checks a music collection for corrupted files. There are no accounts, no dashboard and no contact forms here: the whole site is one page of description with download links, a page about supporting the project, and this page.',
            pp_s1_p2: 'What follows is exactly what happens when the page opens. The list was compiled from the site\u2019s source code, which is open and available for inspection.',
            pp_s2_h: 'What is stored in your browser',
            pp_s2_p1: 'The site stores two values in the browser\u2019s local storage (localStorage):',
            pp_s2_i1: '<code>msi-theme</code> — the theme you picked: light or dark',
            pp_s2_i2: '<code>msi-lang</code> — the page language you picked: Russian or English',
            pp_s2_p2: 'These entries exist only so the page opens the same way on your next visit. They are <b>kept in your browser and sent nowhere</b> — not to the site, not to the author, not to anyone else. You can delete them through your browser settings along with the site data; the page will then take the theme and language from your system and browser again.',
            pp_s2_p3: 'The site does not use cookies.',
            pp_s3_h: 'What requests the page makes',
            pp_s3_p1: 'The site contacts three external addresses. With any such request the browser, by the way networking works, tells the recipient your IP address, browser type and the time of the request — that is an unavoidable part of the internet, not data collection by this site.',
            pp_s3_i1: '<b>GitHub Pages</b> — the site is hosted there, and that is where the browser gets the page itself',
            pp_s3_i2: '<b>Google Fonts</b> (fonts.googleapis.com, fonts.gstatic.com) — the page fonts are loaded from there',
            pp_s3_i3: '<b>api.github.com</b> — one request for the latest version number and file names, so that the download buttons point straight at them; sent only from the main page',
            pp_s3_p2: 'How GitHub and Google handle that data is governed by their own terms: the <a href="https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement">GitHub privacy statement</a> and the <a href="https://policies.google.com/privacy">Google privacy policy</a>. The author of this site has no access to it.',
            pp_s4_h: 'What the site does not do',
            pp_s4_i1: 'No visitor counters and no analytics',
            pp_s4_i2: 'No advertising and no ad networks',
            pp_s4_i3: 'No collection of names, email addresses or any other personal details',
            pp_s4_i4: 'No cross-site tracking and no visitor profiling',
            pp_s4_i5: 'Nothing shared or sold to third parties — there is nothing to share',
            pp_s5_h: 'The program itself',
            pp_s5_p1: 'Music Scan Integrity works without a network connection and only reads files: it deletes nothing, renames nothing and sends nothing anywhere. Settings and the fingerprint database used for corruption tracking stay on your computer — next to the program, or in your user profile if the program folder is not writable.',
            pp_s5_p2: 'You can verify this in the source code: the <a href="https://github.com/J-udgW05/MusicScan">project repository</a> is open.',
            pp_s6_h: 'Links to other sites',
            pp_s6_p1: 'Links from this page lead to GitHub — to the source code, the documentation and the release files. Once you follow them, the rules of that site apply.',
            pp_s7_h: 'Your rights',
            pp_s7_p1: 'The site keeps nothing about you on its side, so there is nothing here to request, correct or delete. Everything the site created sits in your browser, and you remove it yourself by clearing the site data.',
            pp_s7_p2: 'For the data GitHub and Google receive as the owners of those servers, address them directly — the procedure is described in their documents, linked above.',
            pp_s8_h: 'Changes',
            pp_s8_p1: 'If the set of requests or stored values changes, this page changes with it and the date at the top is updated. The edit history is visible in the project repository — it cannot be quietly rewritten after the fact.',
            pp_s9_h: 'Questions',
            pp_s9_p1: 'Questions about this page or the program — <a href="https://github.com/J-udgW05/MusicScan/issues">in the repository Issues</a>.',
            nav_back: 'Back to the site',
            aria_theme_dark: 'Switch to dark theme',
            aria_theme_light: 'Switch to light theme',
            aria_back_top: 'Back to top'
        }
    };


    var lang = root.getAttribute('lang') === 'en' ? 'en' : 'ru';
    var release = null;

    function applyLang(next) {
        lang = next;
        root.setAttribute('lang', next);
        try { localStorage.setItem('msi-lang', next); } catch (e) { }

        var dict = T[next];
        var nodes = document.querySelectorAll('[data-i18n]');
        for (var i = 0; i < nodes.length; i++) {
            var key = nodes[i].getAttribute('data-i18n');
            if (!dict[key]) { continue; }


            if (HTML_KEYS.indexOf(key) === -1) {
                nodes[i].textContent = dict[key];
            } else {
                nodes[i].innerHTML = dict[key];
            }
        }


        var attrNodes = document.querySelectorAll('[data-i18n-content]');
        for (var j = 0; j < attrNodes.length; j++) {
            var attrKey = attrNodes[j].getAttribute('data-i18n-content');
            if (dict[attrKey]) attrNodes[j].setAttribute('content', dict[attrKey]);
        }


        var langOpts = document.querySelectorAll('.lang-option');
        for (var o = 0; o < langOpts.length; o++) {
            var isActive = langOpts[o].getAttribute('data-lang') === next;
            langOpts[o].classList.toggle('active', isActive);
            langOpts[o].setAttribute('aria-checked', isActive ? 'true' : 'false');
        }

        var backTop = document.getElementById('back-to-top');
        if (backTop) backTop.setAttribute('aria-label', dict.aria_back_top);

        markTheme();
        if (release) showRelease(release);
    }

    var langOptions = document.querySelectorAll('.lang-option');
    for (var n = 0; n < langOptions.length; n++) {
        langOptions[n].addEventListener('click', function () {
            var target = this.getAttribute('data-lang');
            if (target !== lang) applyLang(target);
        });
    }


    function isDark() {
        var explicit = root.getAttribute('data-theme');
        if (explicit) return explicit === 'dark';
        return window.matchMedia('(prefers-color-scheme: dark)').matches;
    }

    function markTheme() {
        var btn = document.getElementById('theme');
        if (!btn) return;
        var dark = isDark();
        btn.classList.toggle('is-dark', dark);
        btn.setAttribute('aria-label', dark ? T[lang].aria_theme_light : T[lang].aria_theme_dark);
    }

    var themeBtn = document.getElementById('theme');
    if (themeBtn) {
        themeBtn.addEventListener('click', function () {
            var next = isDark() ? 'light' : 'dark';
            root.setAttribute('data-theme', next);
            try { localStorage.setItem('msi-theme', next); } catch (e) { }
            markTheme();
        });
    }

    var media = window.matchMedia('(prefers-color-scheme: dark)');
    var onSystemChange = function () { if (!root.getAttribute('data-theme')) markTheme(); };
    if (media.addEventListener) media.addEventListener('change', onSystemChange);
    else if (media.addListener) media.addListener(onSystemChange);


    var copyBtn = document.getElementById('copy-email');
    if (copyBtn) {
        var copyTimer = null;

        function showCopied() {
            copyBtn.classList.add('is-copied');
            clearTimeout(copyTimer);
            copyTimer = setTimeout(function () {
                copyBtn.classList.remove('is-copied');
            }, 1600);
        }

        copyBtn.addEventListener('click', function () {
            var text = copyBtn.getAttribute('data-copy');

            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(text).then(showCopied, function () {
                });
                return;
            }


            var ta = document.createElement('textarea');
            ta.value = text;
            ta.style.position = 'fixed';
            ta.style.opacity = '0';
            document.body.appendChild(ta);
            ta.focus();
            ta.select();
            try { document.execCommand('copy'); showCopied(); } catch (e) { }
            document.body.removeChild(ta);
        });
    }


    var backTopBtn = document.getElementById('back-to-top');
    if (backTopBtn) {
        var toggleBackTop = function () {
            backTopBtn.classList.toggle('is-visible', window.pageYOffset > 300);
        };
        window.addEventListener('scroll', toggleBackTop, { passive: true });
        toggleBackTop();


        backTopBtn.addEventListener('click', function () {
            window.scrollTo(0, 0);
        });
    }


    var wordmark = document.querySelector('.wordmark[href="#top"]');
    if (wordmark) {
        wordmark.addEventListener('click', function (e) {
            e.preventDefault();
            window.scrollTo(0, 0);
        });
    }


    function size(bytes) {
        var mb = bytes / 1048576;
        return (mb >= 10 ? Math.round(mb) : Math.round(mb * 10) / 10) + ' MB';
    }

    function showRelease(data) {
        var assets = data.assets || [];
        for (var i = 0; i < assets.length; i++) {
            var a = assets[i];
            var target = null;
            if (/setup\.exe$/i.test(a.name)) target = { file: 'file-setup', link: ['dl-setup', 'dl-main'] };
            else if (/win-x64\.zip$/i.test(a.name)) target = { file: 'file-zip', link: ['dl-zip'] };
            if (!target) continue;

            var fileEl = document.getElementById(target.file);
            if (fileEl) fileEl.textContent = a.name + ' · ' + size(a.size);

            for (var j = 0; j < target.link.length; j++) {
                var linkEl = document.getElementById(target.link[j]);
                if (linkEl) linkEl.setAttribute('href', a.browser_download_url);
            }
        }
    }


    if (window.fetch && document.getElementById('file-setup')) {
        fetch('https://api.github.com/repos/' + REPO + '/releases/latest', { headers: { Accept: 'application/vnd.github+json' } })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (data) { if (data) { release = data; showRelease(data); } })
            .catch(function () {  });
    }


    applyLang(lang);
})();