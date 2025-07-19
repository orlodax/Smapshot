# 🌍✨ Project Purpose: Smapshot - Take a Snapshot of the Map

## ❓ Why Smapshot Exists

Over the years I worked in many different Territory departments.
The drawing (and printing) of the individual maps has always been a thorn in the side, regardless of the exact strategy used.
Meditating on how technology changed and on the need of the maps' end users, I felt the need to create this tool for the following reasons:

### 🤖 Automation

Drawing maps, no matter the software or style used, is intensely time-consuming. ⏰  
Time is probably our most precious resource. Of course we have and want to do whatever needed to keep the work of the whole group going, but should strive to *make the best use of our time* (and let technology do the heavy lifting). 💪

### 🌐 Open Data

The shape of the territory evolves as new roads appear and old ones are changed, houses are built etc.  
It would be awesome to have a tool that always reads the latest cartographic data from the web and uses it to dynamically draw a territory map on the spot!  
Sources like OSM (OpenStreetMap) are constantly updated by the public. They can be updated by us as well based on our observations week after week. 📡✨

### 🔄 Transferability

Putting a lot of time into learning how to draw maps by hand is a risky investment: people move, their availability changes and so on.  
Every time a new volunteer starts collaborating, they need to be taught from scratch.  
Also, if a trained volunteer moves, will they really be able to use their acquired skills?

**With Smapshot, anyone can create maps — no special skills required!** 🎉

### 🆕 New Requirements

Since the cartographic data is entirely available online, the need for a physical maps archive in the KH no longer exists, nor does the need to store individual graphic assets like one PDF printable map for each territory map.

**The sources of information are just 2:**

- 📋 The overall border KML privately provided by the Branch, along with the individual maps' borders (KML files)
- 🌐 The online public cartographic data

The first is all that needs to be kept in archive 💾  
These files, unlike the printable maps, are lightweight and can be stored in any app (for instance Hourglass) or digital archive (DropBox, Google Drive, etc)

**Let me stress the new requirements part.** ⚡

Realistically, we still need printed maps. 🖨️

At the same time, in case we would all start using digital maps on our phones, **they will not have to be printable PDFs** or other static image types. Modern apps visualize the KML area superimposed on existing OSM/Google Earth maps, just like (and better than) Hourglass does.

Therefore, waiting for the day when we might have an official territory map tool to install on our phones, directly integrated with the local maps archive, I felt the need to produce this tool that "flashes" the existing cartographic data and sets it ready for printing: "Smapshot" 📸🗺️

## 🚀 What This Means

The workflow with this tool is extremely simple:

1️⃣ **Obtain** the individual map's KML file from whatever source they are kept in  
2️⃣ **Drag** the KML file onto the executable file "Smapshot" 🎯  
3️⃣ **Observe** the PDF appear after few seconds ⚡  
4️⃣ **Print** it! (it is already an A4 page with layout precalculated to best fit the page) 🖨️

**As per the points above, this process:**

✅ Does not require any skill  
⚡ Is very fast  
🗺️ Always produces a ready-to-print map, up-to-date with current cartographic data  
🌍 Can be used everywhere

Additionally, the software allows for the configuration of the appearance of the end-result PDF map to fine-tune things such as colors, roads thickness, text size, etc. 🎨🔧

## 🛠️ Current State

The tool is currently in its **beta stage**. 🚧

This means that the core functionality is in place, but there is still some work to do to cover all scenarios and make the map look perfect without missing important elements or labels in all different areas.  
In my opinion it works quite well already with the rural maps! 🌳🗺️

I might keep working on it, and the code is public: anyone is welcome to address issues or add features using the available tools, frameworks, and procedures for this type of app. 👨‍💻👩‍💻

## 🎯 Additional Use Case

Even if better ways to provide maps other than Smapshot are in use, it can still be super useful to quickly draft maps of any given area of interest!
All you need to do is define a perimeter in a KML file and feed it to the program. 📂➡️🖨️

**Real-Life Example:**

🏢 The group is assigned **territory number 34** for the next few weeks  
✂️ You'd like to split it into **4-5 chunks** to assign to different car groups  
🗺️ Once you've decided on the shape and extension of each area you want to assign, just create a KML perimeter for each one on your computer and feed them to Smapshot  

**Result:** You'll obtain **ready-to-print map excerpts** to be used in a "once-off" fashion! 📄✨

---

> *Made with ❤️ for territory managers and map lovers everywhere!* 🌍✨
---

## RUSSIAN

## 🌍✨ Цель проекта: Smapshot - Сделай снимок карты

## ❓ Зачем существует Smapshot

На протяжении многих лет я работал в различных территориальных отделах.
Рисование (и печать) отдельных карт всегда было занозой в боку, независимо от используемой стратегии.
Размышляя о том, как изменились технологии и потребности конечных пользователей карт, я почувствовал необходимость создать этот инструмент по следующим причинам:

### 🤖 Автоматизация

Рисование карт, независимо от используемого программного обеспечения или стиля, отнимает очень много времени. ⏰  
Время - вероятно, наш самый ценный ресурс. Конечно, мы должны и хотим делать все необходимое для поддержания работы всей группы, но должны стремиться к *лучшему использованию нашего времени* (и позволить технологиям делать тяжелую работу). 💪

### 🌐 Открытые данные

Форма территории развивается по мере появления новых дорог и изменения старых, строительства домов и т.д.  
Было бы здорово иметь инструмент, который всегда считывает последние картографические данные из интернета и использует их для динамического рисования карты территории на месте!  
Источники, такие как OSM (OpenStreetMap), постоянно обновляются общественностью. Они также могут обновляться нами на основе наших наблюдений неделя за неделей. 📡✨

### 🔄 Передаваемость

Вкладывать много времени в изучение рисования карт вручную - рискованная инвестиция: люди переезжают, их доступность меняется и так далее.  
Каждый раз, когда новый доброволец начинает сотрудничать, его нужно обучать с нуля.  
Кроме того, если обученный доброволец переедет, сможет ли он действительно использовать свои приобретенные навыки?

**С Smapshot любой может создавать карты — никаких специальных навыков не требуется!** 🎉

### 🆕 Новые требования

Поскольку картографические данные полностью доступны онлайн, потребность в физическом архиве карт в ЦС больше не существует, как и потребность в хранении отдельных графических активов, таких как один печатный PDF для каждой карты территории.

**Источников информации всего 2:**

- 📋 Общий пограничный KML, предоставляемый частным образом Филиалом, вместе с границами отдельных карт (файлы KML)
- 🌐 Онлайн публичные картографические данные

Первое - это все, что нужно хранить в архиве 💾  
Эти файлы, в отличие от печатных карт, легкие и могут храниться в любом приложении (например, Hourglass) или цифровом архиве (DropBox, Google Drive и т.д.)

**Позвольте мне подчеркнуть часть о новых требованиях.** ⚡

Реалистично говоря, нам все еще нужны печатные карты. 🖨️

В то же время, если мы все начнем использовать цифровые карты на наших телефонах, **они не должны быть печатными PDF** или другими статическими типами изображений. Современные приложения визуализируют область KML, наложенную на существующие карты OSM/Google Earth, точно так же, как (и лучше, чем) делает Hourglass.

Поэтому, ожидая дня, когда у нас может появиться официальный инструмент карт территорий для установки на наши телефоны, напрямую интегрированный с локальным архивом карт, я почувствовал необходимость создать этот инструмент, который "вспыхивает" существующими картографическими данными и готовит их к печати: "Smapshot" 📸🗺️

## 🚀 Что это означает

Рабочий процесс с этим инструментом чрезвычайно прост:

1️⃣ **Получите** файл KML отдельной карты из любого источника, где они хранятся  
2️⃣ **Перетащите** файл KML на исполняемый файл "Smapshot" 🎯  
3️⃣ **Наблюдайте**, как PDF появляется через несколько секунд ⚡  
4️⃣ **Распечатайте** его! (это уже страница A4 с предварительно рассчитанным макетом для наилучшего размещения на странице) 🖨️

**Согласно вышеизложенным пунктам, этот процесс:**

✅ Не требует никаких навыков  
⚡ Очень быстрый  
🗺️ Всегда производит готовую к печати карту, актуальную с текущими картографическими данными  
🌍 Может использоваться везде

Дополнительно, программное обеспечение позволяет настраивать внешний вид итогового PDF-файла карты для тонкой настройки таких вещей, как цвета, толщина дорог, размер текста и т.д. 🎨🔧

## 🛠️ Текущее состояние

Инструмент в настоящее время находится на **бета-стадии**. 🚧

Это означает, что основная функциональность на месте, но еще есть работа, которую нужно выполнить, чтобы охватить все сценарии и сделать карту идеальной без пропуска важных элементов или меток в различных областях.  
По моему мнению, он уже довольно хорошо работает с сельскими картами! 🌳🗺️

Я могу продолжить работу над ним, и код открыт: любой может решать проблемы или добавлять функции, используя доступные инструменты, фреймворки и процедуры для этого типа приложений. 👨‍💻👩‍💻

## 🎯 Дополнительный случай использования

Даже если используются лучшие способы предоставления карт, чем Smapshot, он все еще может быть очень полезен для быстрого создания черновиков карт любой интересующей области! 🚀  
Все, что вам нужно сделать, это определить периметр в файле KML и подать его в программу. 📂➡️🖨️

**Реальный пример:**

🏢 Группе назначена **территория номер 34** на следующие несколько недель  
✂️ Вы хотели бы разделить ее на **4-5 частей** для назначения разным автомобильным группам  
🗺️ После того, как вы решили о форме и протяженности каждой области, которую хотите назначить, просто создайте периметр KML для каждой на своем компьютере и подайте их в Smapshot  

**Результат:** Вы получите **готовые к печати фрагменты карт** для использования в "разовом" порядке! 📄✨

---

> *Сделано с ❤️ для территориальных менеджеров и любителей карт по всему миру!* 🌍✨

---

## UKRAINIAN

## 🌍✨ Мета проекту: Smapshot - Зроби знімок карти

## ❓ Навіщо існує Smapshot

Протягом багатьох років я працював у різних територіальних відділах.
Малювання (та друк) окремих карт завжди було каменем спотикання, незалежно від використовуваної стратегії.
Роздумуючи про те, як змінилися технології та потреби кінцевих користувачів карт, я відчув необхідність створити цей інструмент з наступних причин:

### 🤖 Автоматизація

Малювання карт, незалежно від використовуваного програмного забезпечення чи стилю, займає дуже багато часу. ⏰  
Час - ймовірно, наш найцінніший ресурс. Звичайно, ми повинні і хочемо робити все необхідне для підтримки роботи всієї групи, але повинні прагнути до *кращого використання нашого часу* (і дозволити технологіям робити важку роботу). 💪

### 🌐 Відкриті дані

Форма території розвивається з появою нових доріг та зміною старих, будівництвом будинків тощо.  
Було б чудово мати інструмент, який завжди зчитує найновіші картографічні дані з інтернету та використовує їх для динамічного малювання карти території на місці!  
Джерела, такі як OSM (OpenStreetMap), постійно оновлюються громадськістю. Вони також можуть оновлюватися нами на основі наших спостережень тиждень за тижнем. 📡✨

### 🔄 Передаваність

Витрачати багато часу на вивчення малювання карт вручну - ризикована інвестиція: люди переїжджають, їхня доступність змінюється тощо.  
Кожного разу, коли новий волонтер починає співпрацювати, його потрібно навчати з нуля.  
Крім того, якщо навчений волонтер переїде, чи зможе він справді використовувати свої набуті навички?

**З Smapshot будь-хто може створювати карти — ніяких спеціальних навичок не потрібно!** 🎉

### 🆕 Нові вимоги

Оскільки картографічні дані повністю доступні онлайн, потреба у фізичному архіві карт у КЗ більше не існує, як і потреба у зберіганні окремих графічних активів, таких як один друкований PDF для кожної карти території.

**Джерел інформації лише 2:**

- 📋 Загальний межовий KML, що надається приватно Філією, разом з межами окремих карт (файли KML)
- 🌐 Онлайн публічні картографічні дані

Перше - це все, що потрібно зберігати в архіві 💾  
Ці файли, на відміну від друкованих карт, легкі і можуть зберігатися в будь-якому додатку (наприклад, Hourglass) або цифровому архіві (DropBox, Google Drive тощо)

**Дозвольте мені підкреслити частину про нові вимоги.** ⚡

Реалістично кажучи, нам все ще потрібні друковані карти. 🖨️

Водночас, якщо ми всі почнемо використовувати цифрові карти на наших телефонах, **вони не повинні бути друкованими PDF** або іншими статичними типами зображень. Сучасні додатки візуалізують область KML, накладену на існуючі карти OSM/Google Earth, точно так само, як (і краще, ніж) робить Hourglass.

Тому, очікуючи дня, коли у нас може з'явитися офіційний інструмент карт територій для встановлення на наші телефони, безпосередньо інтегрований з локальним архівом карт, я відчув необхідність створити цей інструмент, який "спалахує" існуючими картографічними даними та готує їх до друку: "Smapshot" 📸🗺️

## 🚀 Що це означає

Робочий процес з цим інструментом надзвичайно простий:

1️⃣ **Отримайте** файл KML окремої карти з будь-якого джерела, де вони зберігаються  
2️⃣ **Перетягніть** файл KML на виконуваний файл "Smapshot" 🎯  
3️⃣ **Спостерігайте**, як PDF з'являється через кілька секунд ⚡  
4️⃣ **Роздрукуйте** його! (це вже сторінка A4 з попередньо розрахованим макетом для найкращого розміщення на сторінці) 🖨️

**Згідно з вищезазначеними пунктами, цей процес:**

✅ Не вимагає жодних навичок  
⚡ Дуже швидкий  
🗺️ Завжди виробляє готову до друку карту, актуальну з поточними картографічними даними  
🌍 Може використовуватися скрізь

Додатково, програмне забезпечення дозволяє налаштовувати зовнішній вигляд підсумкового PDF-файлу карти для тонкого налаштування таких речей, як кольори, товщина доріг, розмір тексту тощо. 🎨🔧

## 🛠️ Поточний стан

Інструмент наразі знаходиться на **бета-стадії**. 🚧

Це означає, що основна функціональність на місці, але ще є робота, яку потрібно виконати, щоб охопити всі сценарії та зробити карту ідеальною без пропуску важливих елементів або міток у різних областях.  
На мою думку, він уже досить добре працює з сільськими картами! 🌳🗺️

Я можу продовжити роботу над ним, і код відкритий: будь-хто може вирішувати проблеми або додавати функції, використовуючи доступні інструменти, фреймворки та процедури для цього типу додатків. 👨‍💻👩‍💻

## 🎯 Додатковий випадок використання

Навіть якщо використовуються кращі способи надання карт, ніж Smapshot, він все ще може бути дуже корисним для швидкого створення чернеток карт будь-якої цікавої області! 🚀  
Все, що вам потрібно зробити, це визначити периметр у файлі KML та подати його в програму. 📂➡️🖨️

**Реальний приклад:**

🏢 Групі призначена **територія номер 34** на наступні кілька тижнів  
✂️ Ви хотіли б розділити її на **4-5 частин** для призначення різним автомобільним групам  
🗺️ Після того, як ви вирішили про форму та протяжність кожної області, яку хочете призначити, просто створіть периметр KML для кожної на своєму комп'ютері та подайте їх у Smapshot  

**Результат:** Ви отримаєте **готові до друку фрагменти карт** для використання в "разовому" порядку! 📄✨

---

> *Зроблено з ❤️ для територіальних менеджерів та любителів карт по всьому світу!* 🌍✨

---

## PORTUGUESE

## 🌍✨ Propósito do Projeto: Smapshot - Tire uma Foto do Mapa

## ❓ Por que o Smapshot existe

Ao longo dos anos trabalhei em muitos departamentos territoriais diferentes.
O desenho (e impressão) dos mapas individuais sempre foi um espinho no lado, independentemente da estratégia exata usada.
Meditando sobre como a tecnologia mudou e sobre a necessidade dos usuários finais dos mapas, senti a necessidade de criar esta ferramenta pelas seguintes razões:

### 🤖 Automação

Desenhar mapas, independentemente do software e estilo utilizados, consome muito tempo. ⏰  
O tempo é provavelmente nosso recurso mais precioso. Claro que temos e queremos fazer tudo o que for necessário para manter o trabalho de todo o grupo funcionando, mas devemos nos esforçar para *fazer o melhor uso do nosso tempo* (e deixar a tecnologia fazer o trabalho pesado). 💪

### 🌐 Dados Abertos

A forma do território evolui à medida que novas estradas aparecem e antigas são alteradas, casas são construídas, etc.  
Seria incrível ter uma ferramenta que sempre lê os dados cartográficos mais recentes da web e os usa para desenhar dinamicamente um mapa territorial no local!  
Fontes como OSM (OpenStreetMap) são constantemente atualizadas pelo público. Elas também podem ser atualizadas por nós com base em nossas observações semana após semana. 📡✨

### 🔄 Transferibilidade

Investir muito tempo aprendendo a desenhar mapas à mão é um investimento arriscado: as pessoas se mudam, sua disponibilidade muda e assim por diante.  
Toda vez que um novo voluntário começa a colaborar, eles precisam ser ensinados do zero.  
Além disso, se um voluntário treinado se mudar, ele realmente será capaz de usar suas habilidades adquiridas?

**Com o Smapshot, qualquer pessoa pode criar mapas — nenhuma habilidade especial é necessária!** 🎉

### 🆕 Novos Requisitos

Como os dados cartográficos estão totalmente disponíveis online, a necessidade de um arquivo físico de mapas no SL não existe mais, nem existe a necessidade de armazenar ativos gráficos individuais como um PDF imprimível para cada mapa territorial.

**As fontes de informação são apenas 2:**

- 📋 O KML de fronteira geral fornecido privativamente pela Filial, junto com as fronteiras dos mapas individuais (arquivos KML)
- 🌐 Os dados cartográficos públicos online

O primeiro é tudo o que precisa ser mantido no arquivo 💾  
Esses arquivos, ao contrário dos mapas impressos, são leves e podem ser armazenados em qualquer aplicativo (por exemplo, Hourglass) ou arquivo digital (DropBox, Google Drive, etc.)

**Deixe-me enfatizar a parte dos novos requisitos.** ⚡

Realisticamente, ainda precisamos de mapas impressos. 🖨️

Ao mesmo tempo, caso todos começássemos a usar mapas digitais em nossos telefones, **eles não precisariam ser PDFs imprimíveis** ou outros tipos de imagem estática. Aplicativos modernos visualizam a área KML sobreposta em mapas OSM/Google Earth existentes, exatamente como (e melhor que) o Hourglass faz.

Portanto, esperando pelo dia em que possamos ter uma ferramenta oficial de mapas territoriais para instalar em nossos telefones, diretamente integrada com o arquivo local de mapas, senti a necessidade de produzir esta ferramenta que "pisca" os dados cartográficos existentes e os prepara para impressão: "Smapshot" 📸🗺️

## 🚀 O que isso significa

O fluxo de trabalho com esta ferramenta é extremamente simples:

1️⃣ **Obtenha** o arquivo KML do mapa individual de qualquer fonte onde eles estão mantidos  
2️⃣ **Arraste** o arquivo KML para o arquivo executável "Smapshot" 🎯  
3️⃣ **Observe** o PDF aparecer após alguns segundos ⚡  
4️⃣ **Imprima** ele! (já é uma página A4 com layout pré-calculado para melhor ajustar à página) 🖨️

**De acordo com os pontos acima, este processo:**

✅ Não requer nenhuma habilidade  
⚡ É muito rápido  
🗺️ Sempre produz um mapa pronto para impressão, atualizado com os dados cartográficos atuais  
🌍 Pode ser usado em qualquer lugar

Adicionalmente, o software permite a configuração da aparência do arquivo PDF final do mapa para ajustar finamente coisas como cores, espessura das estradas, tamanho do texto, etc. 🎨🔧

## 🛠️ Estado Atual

A ferramenta está atualmente em seu **estágio beta**. 🚧

Isso significa que a funcionalidade principal está no lugar, mas ainda há trabalho a ser feito para cobrir todos os cenários e fazer o mapa parecer perfeito sem perder elementos importantes ou rótulos em diferentes áreas.  
Na minha opinião, já funciona muito bem com mapas rurais! 🌳🗺️

Posso continuar trabalhando nele, e o código é público: qualquer pessoa é bem-vinda para abordar problemas ou adicionar recursos usando as ferramentas, frameworks e procedimentos disponíveis para este tipo de aplicativo. 👨‍💻👩‍💻

## 🎯 Caso de Uso Adicional

Mesmo se melhores maneiras de fornecer mapas além do Smapshot estiverem em uso, ainda pode ser super útil para rapidamente esboçar mapas de qualquer área de interesse! 🚀  
Tudo o que você precisa fazer é definir um perímetro em um arquivo KML e alimentá-lo no programa. 📂➡️🖨️

**Exemplo da Vida Real:**

🏢 O grupo recebe o **território número 34** pelas próximas semanas  
✂️ Você gostaria de dividi-lo em **4-5 pedaços** para atribuir a diferentes grupos de carros  
🗺️ Uma vez que você decidiu sobre a forma e extensão de cada área que quer atribuir, apenas crie um perímetro KML para cada um no seu computador e alimente-os no Smapshot  

**Resultado:** Você obterá **trechos de mapa prontos para impressão** para serem usados de forma "única"! 📄✨

---

> *Feito com ❤️ para gerentes territoriais e amantes de mapas em todo o mundo!* 🌍✨
