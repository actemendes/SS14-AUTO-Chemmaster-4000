# Офлайн-тесты Химмастера

Результат: **519/519 пройдено**, ошибок: 0.

SS220: `86d0f7bffb5f3f4d3ee7bef3b9080c2e37b7ec03`. Правил реакций: 345. Прототипов веществ: 453.

Игра и память игрового процесса не использовались. Калибровка проверена структурно и не изменена.

| Группа | Пройдено | Всего |
|---|---:|---:|
| calibration | 16 | 16 |
| source | 1 | 1 |
| rows | 8 | 8 |
| transfer | 5 | 5 |
| reactions | 7 | 7 |
| production | 18 | 18 |
| capacity | 6 | 6 |
| catalyst | 2 | 2 |
| selected-medicines | 75 | 75 |
| stock-matrix | 256 | 256 |
| seed-4000 | 100 | 100 |
| guards | 25 | 25 |

## Что это проверяет

Наличие и нехватку исходников/промежуточных лекарств, целые партии, повторные заказы, возврат в буфер, четыре сортировки, перестановку строк, вместимость, реакции между кликами, катализаторы и остановку при изменении состояния.

## Граница достоверности

Это независимая модель по публичным исходникам, не запуск игрового сервера. Эффекты, внешнее оборудование, фасовка и drag-and-drop не реализованы. Проверка координат не заменяет проверку живых кнопок и фактической прокрутки. Версия сервера и runtime-изменения прототипов не проверялись.

## Все сценарии

| Результат | Группа | Сценарий |
|---|---|---|
| PASS | calibration | Полная фиксированная тестовая разметка 1000×900  |
| PASS | calibration | Виртуальные строки не принимаются за живое UI  |
| PASS | calibration | Все дозировки первой строки: 1  |
| PASS | calibration | Все дозировки первой строки: 5  |
| PASS | calibration | Все дозировки первой строки: 10  |
| PASS | calibration | Все дозировки первой строки: 15  |
| PASS | calibration | Все дозировки первой строки: 20  |
| PASS | calibration | Все дозировки первой строки: 25  |
| PASS | calibration | Все дозировки первой строки: 30  |
| PASS | calibration | Все дозировки первой строки: 50  |
| PASS | calibration | Все дозировки первой строки: 75  |
| PASS | calibration | Все дозировки первой строки: 100  |
| PASS | calibration | Все дозировки первой строки: all  |
| PASS | calibration | Вещество на 40-й строке требует прокрутки  |
| PASS | calibration | Обрезанная нижняя строка не используется  |
| PASS | source | Зафиксированные игровые данные: 345 реакций, 453 вещества  |
| PASS | rows | Порядок и стабильные равные количества: none  |
| PASS | rows | Порядок и стабильные равные количества: alphabetical  |
| PASS | rows | Порядок и стабильные равные количества: quantity  |
| PASS | rows | Порядок и стабильные равные количества: latest  |
| PASS | rows | Удаление в буфере сохраняет порядок остальных  |
| PASS | rows | Удаление из мензурки переносит последнюю строку на место первой  |
| PASS | rows | Пополнение существующей строки не перемещает её в конец  |
| PASS | rows | Сортировка latest — обратный raw, не время последнего пополнения  |
| PASS | transfer | «Всё» относится только к одному веществу  |
| PASS | transfer | Доза ограничена свободным объёмом  |
| PASS | transfer | Доза ограничена наличием реагента  |
| PASS | transfer | Точная дробь через «Всё» и обратно  |
| PASS | transfer | Буфер не смешивается при возврате через кнопки  |
| PASS | reactions | Смесь реагирует сразу после добавления второй части  |
| PASS | reactions | Наивные четыре клика углерода портят инапровалин  |
| PASS | reactions | Несовместимые brute-лекарства превращаются в Razorium  |
| PASS | reactions | Непрерывный катализатор: достаточно 0.01u плазмы  |
| PASS | reactions | FixedPoint2: 0.01u кислорода не хватает на дробь 0.005 реакции  |
| PASS | reactions | Минимальная температура включительна в серверной логике  |
| PASS | production | Готовое лекарство — ни одного клика  |
| PASS | production | Частичный запас — готовить только недостающее  |
| PASS | production | Два готовых промежуточных препарата  |
| PASS | production | Инапровалин 12u — безопасные малые партии вместо побочного бикаридина  |
| PASS | production | Округление промежуточных партий 5u → 6u и возврат остатков  |
| PASS | production | Резерв целей: диловен нельзя целиком отдать в трикордразин  |
| PASS | production | Повторяющиеся цели суммируются  |
| PASS | production | Повторяющиеся малые цели округляются одной общей партией  |
| PASS | production | Пять последовательных заказов с пополнением буфера  |
| PASS | production | Сторонний игрок добавил новый реагент между заказами  |
| PASS | capacity | Бикаридин 120u через ёмкость 3  |
| PASS | capacity | Бикаридин 120u через ёмкость 5  |
| PASS | capacity | Бикаридин 120u через ёмкость 10  |
| PASS | capacity | Бикаридин 120u через ёмкость 30  |
| PASS | capacity | Бикаридин 120u через ёмкость 50  |
| PASS | capacity | Бикаридин 120u через ёмкость 100  |
| PASS | catalyst | Дексалин с расширением объёма и многократным возвратом плазмы  |
| PASS | catalyst | Зарезервированная цель может быть временным катализатором  |
| PASS | production | Готовый нагреваемый препарат не требует повторного нагрева  |
| PASS | production | Эпинефрин из готовых компонентов  |
| PASS | production | Несколько лекарств в одном заказе  |
| PASS | production | Побочный продукт крови тоже возвращается отдельной строкой  |
| PASS | production | Побочный выход удовлетворяет вторую выбранную цель  |
| PASS | production | Доступная безопасная альтернатива предпочтительнее взрывной  |
| PASS | production | Рвотное: выход 2u из 3u исходников  |
| PASS | production | Лепоразин: готовый ферросилиций и повторное использование катализатора  |
| PASS | reactions | Эффект реакции воды с калием не выдаётся за обычное смешивание  |
| PASS | selected-medicines | Epinephrine: отсутствует и нет исходников  |
| PASS | selected-medicines | Epinephrine: уже есть ровно 10u  |
| PASS | selected-medicines | Tricordrazine: отсутствует и нет исходников  |
| PASS | selected-medicines | Tricordrazine: уже есть ровно 10u  |
| PASS | selected-medicines | Bicaridine: отсутствует и нет исходников  |
| PASS | selected-medicines | Bicaridine: уже есть ровно 10u  |
| PASS | selected-medicines | Omnizine: отсутствует и нет исходников  |
| PASS | selected-medicines | Omnizine: уже есть ровно 10u  |
| PASS | selected-medicines | Bruizine: отсутствует и нет исходников  |
| PASS | selected-medicines | Bruizine: уже есть ровно 10u  |
| PASS | selected-medicines | Lacerinol: отсутствует и нет исходников  |
| PASS | selected-medicines | Lacerinol: уже есть ровно 10u  |
| PASS | selected-medicines | Puncturase: отсутствует и нет исходников  |
| PASS | selected-medicines | Puncturase: уже есть ровно 10u  |
| PASS | selected-medicines | Kelotane: отсутствует и нет исходников  |
| PASS | selected-medicines | Kelotane: уже есть ровно 10u  |
| PASS | selected-medicines | Dermaline: отсутствует и нет исходников  |
| PASS | selected-medicines | Dermaline: уже есть ровно 10u  |
| PASS | selected-medicines | Pyrazine: отсутствует и нет исходников  |
| PASS | selected-medicines | Pyrazine: уже есть ровно 10u  |
| PASS | selected-medicines | Insuzine: отсутствует и нет исходников  |
| PASS | selected-medicines | Insuzine: уже есть ровно 10u  |
| PASS | selected-medicines | Leporazine: отсутствует и нет исходников  |
| PASS | selected-medicines | Leporazine: уже есть ровно 10u  |
| PASS | selected-medicines | Sigynate: отсутствует и нет исходников  |
| PASS | selected-medicines | Sigynate: уже есть ровно 10u  |
| PASS | selected-medicines | Siderlac: отсутствует и нет исходников  |
| PASS | selected-medicines | Siderlac: уже есть ровно 10u  |
| PASS | selected-medicines | Phalanximine: отсутствует и нет исходников  |
| PASS | selected-medicines | Phalanximine: уже есть ровно 10u  |
| PASS | selected-medicines | Ambuzol: отсутствует и нет исходников  |
| PASS | selected-medicines | Ambuzol: уже есть ровно 10u  |
| PASS | selected-medicines | AmbuzolPlus: отсутствует и нет исходников  |
| PASS | selected-medicines | AmbuzolPlus: уже есть ровно 10u  |
| PASS | selected-medicines | Dexalin: отсутствует и нет исходников  |
| PASS | selected-medicines | Dexalin: уже есть ровно 10u  |
| PASS | selected-medicines | DexalinPlus: отсутствует и нет исходников  |
| PASS | selected-medicines | DexalinPlus: уже есть ровно 10u  |
| PASS | selected-medicines | Inaprovaline: отсутствует и нет исходников  |
| PASS | selected-medicines | Inaprovaline: уже есть ровно 10u  |
| PASS | selected-medicines | Dylovene: отсутствует и нет исходников  |
| PASS | selected-medicines | Dylovene: уже есть ровно 10u  |
| PASS | selected-medicines | Diphenhydramine: отсутствует и нет исходников  |
| PASS | selected-medicines | Diphenhydramine: уже есть ровно 10u  |
| PASS | selected-medicines | Stellibinin: отсутствует и нет исходников  |
| PASS | selected-medicines | Stellibinin: уже есть ровно 10u  |
| PASS | selected-medicines | Ethylredoxrazine: отсутствует и нет исходников  |
| PASS | selected-medicines | Ethylredoxrazine: уже есть ровно 10u  |
| PASS | selected-medicines | Arithrazine: отсутствует и нет исходников  |
| PASS | selected-medicines | Arithrazine: уже есть ровно 10u  |
| PASS | selected-medicines | Hyronalin: отсутствует и нет исходников  |
| PASS | selected-medicines | Hyronalin: уже есть ровно 10u  |
| PASS | selected-medicines | Cryoxadone: отсутствует и нет исходников  |
| PASS | selected-medicines | Cryoxadone: уже есть ровно 10u  |
| PASS | selected-medicines | Doxarubixadone: отсутствует и нет исходников  |
| PASS | selected-medicines | Doxarubixadone: уже есть ровно 10u  |
| PASS | selected-medicines | Opporozidone: отсутствует и нет исходников  |
| PASS | selected-medicines | Opporozidone: уже есть ровно 10u  |
| PASS | selected-medicines | Aloxadone: отсутствует и нет исходников  |
| PASS | selected-medicines | Aloxadone: уже есть ровно 10u  |
| PASS | selected-medicines | Necrosol: отсутствует и нет исходников  |
| PASS | selected-medicines | Cerebrin: отсутствует и нет исходников  |
| PASS | selected-medicines | Cerebrin: уже есть ровно 10u  |
| PASS | selected-medicines | Haloperidol: отсутствует и нет исходников  |
| PASS | selected-medicines | Haloperidol: уже есть ровно 10u  |
| PASS | selected-medicines | Mannitol: отсутствует и нет исходников  |
| PASS | selected-medicines | Mannitol: уже есть ровно 10u  |
| PASS | selected-medicines | Psicodine: отсутствует и нет исходников  |
| PASS | selected-medicines | Psicodine: уже есть ровно 10u  |
| PASS | selected-medicines | Ipecac: отсутствует и нет исходников  |
| PASS | selected-medicines | Ipecac: уже есть ровно 10u  |
| PASS | selected-medicines | Cognizine: отсутствует и нет исходников  |
| PASS | selected-medicines | Cognizine: уже есть ровно 10u  |
| PASS | selected-medicines | Oculine: отсутствует и нет исходников  |
| PASS | selected-medicines | Oculine: уже есть ровно 10u  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=00, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=01, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=02, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=03, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=04, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=05, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=06, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=07, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=08, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=09, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=10, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=11, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=12, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=13, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=14, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=15, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=16, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=17, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=18, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=19, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=20, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=21, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=22, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=23, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=24, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=25, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=26, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=27, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=28, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=29, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=30, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=31, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=32, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=33, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=34, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=35, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=36, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=37, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=38, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=39, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=40, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=41, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=42, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=43, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=44, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=45, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=46, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=47, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=48, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=49, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=50, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=51, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=52, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=53, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=54, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=55, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=56, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=57, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=58, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=59, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=60, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=61, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=62, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=0, маска шести исходников=63, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=00, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=01, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=02, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=03, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=04, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=05, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=06, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=07, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=08, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=09, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=10, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=11, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=12, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=13, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=14, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=15, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=16, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=17, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=18, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=19, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=20, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=21, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=22, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=23, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=24, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=25, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=26, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=27, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=28, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=29, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=30, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=31, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=32, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=33, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=34, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=35, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=36, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=37, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=38, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=39, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=40, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=41, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=42, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=43, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=44, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=45, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=46, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=47, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=48, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=49, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=50, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=51, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=52, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=53, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=54, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=55, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=56, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=57, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=58, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=59, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=60, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=61, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=62, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=1, маска шести исходников=63, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=00, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=01, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=02, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=03, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=04, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=05, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=06, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=07, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=08, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=09, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=10, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=11, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=12, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=13, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=14, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=15, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=16, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=17, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=18, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=19, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=20, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=21, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=22, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=23, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=24, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=25, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=26, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=27, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=28, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=29, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=30, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=31, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=32, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=33, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=34, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=35, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=36, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=37, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=38, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=39, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=40, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=41, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=42, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=43, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=44, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=45, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=46, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=47, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=48, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=49, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=50, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=51, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=52, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=53, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=54, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=55, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=56, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=57, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=58, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=59, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=60, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=61, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=62, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=2, маска шести исходников=63, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=00, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=01, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=02, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=03, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=04, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=05, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=06, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=07, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=08, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=09, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=10, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=11, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=12, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=13, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=14, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=15, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=16, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=17, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=18, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=19, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=20, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=21, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=22, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=23, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=24, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=25, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=26, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=27, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=28, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=29, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=30, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=31, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=32, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=33, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=34, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=35, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=36, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=37, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=38, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=39, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=40, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=41, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=42, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=43, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=44, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=45, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=46, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=47, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=48, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=49, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=50, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=51, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=52, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=53, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=54, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=55, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=56, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=57, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=58, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=59, sort=latest  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=60, sort=none  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=61, sort=alphabetical  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=62, sort=quantity  |
| PASS | stock-matrix | Трикордразин: готовые D/I=3, маска шести исходников=63, sort=latest  |
| PASS | seed-4000 | Бикаридин #0: цель 43, запас 14, ёмкость 50, sort=none  |
| PASS | seed-4000 | Бикаридин #1: цель 18, запас 25, ёмкость 3, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #2: цель 87, запас 3, ёмкость 3, sort=quantity  |
| PASS | seed-4000 | Бикаридин #3: цель 20, запас 0, ёмкость 10, sort=latest  |
| PASS | seed-4000 | Бикаридин #4: цель 40, запас 15, ёмкость 100, sort=none  |
| PASS | seed-4000 | Бикаридин #5: цель 12, запас 12, ёмкость 10, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #6: цель 70, запас 27, ёмкость 100, sort=quantity  |
| PASS | seed-4000 | Бикаридин #7: цель 89, запас 3, ёмкость 30, sort=latest  |
| PASS | seed-4000 | Бикаридин #8: цель 48, запас 9, ёмкость 30, sort=none  |
| PASS | seed-4000 | Бикаридин #9: цель 33, запас 11, ёмкость 2, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #10: цель 68, запас 27, ёмкость 30, sort=quantity  |
| PASS | seed-4000 | Бикаридин #11: цель 9, запас 16, ёмкость 10, sort=latest  |
| PASS | seed-4000 | Бикаридин #12: цель 39, запас 11, ёмкость 10, sort=none  |
| PASS | seed-4000 | Бикаридин #13: цель 13, запас 23, ёмкость 10, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #14: цель 55, запас 21, ёмкость 50, sort=quantity  |
| PASS | seed-4000 | Бикаридин #15: цель 21, запас 26, ёмкость 100, sort=latest  |
| PASS | seed-4000 | Бикаридин #16: цель 74, запас 7, ёмкость 100, sort=none  |
| PASS | seed-4000 | Бикаридин #17: цель 1, запас 21, ёмкость 2, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #18: цель 16, запас 5, ёмкость 50, sort=quantity  |
| PASS | seed-4000 | Бикаридин #19: цель 84, запас 16, ёмкость 100, sort=latest  |
| PASS | seed-4000 | Бикаридин #20: цель 50, запас 17, ёмкость 100, sort=none  |
| PASS | seed-4000 | Бикаридин #21: цель 53, запас 18, ёмкость 50, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #22: цель 2, запас 20, ёмкость 2, sort=quantity  |
| PASS | seed-4000 | Бикаридин #23: цель 69, запас 2, ёмкость 100, sort=latest  |
| PASS | seed-4000 | Бикаридин #24: цель 69, запас 26, ёмкость 10, sort=none  |
| PASS | seed-4000 | Бикаридин #25: цель 14, запас 8, ёмкость 50, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #26: цель 58, запас 1, ёмкость 2, sort=quantity  |
| PASS | seed-4000 | Бикаридин #27: цель 85, запас 27, ёмкость 100, sort=latest  |
| PASS | seed-4000 | Бикаридин #28: цель 48, запас 25, ёмкость 30, sort=none  |
| PASS | seed-4000 | Бикаридин #29: цель 73, запас 9, ёмкость 50, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #30: цель 40, запас 11, ёмкость 10, sort=quantity  |
| PASS | seed-4000 | Бикаридин #31: цель 5, запас 26, ёмкость 30, sort=latest  |
| PASS | seed-4000 | Бикаридин #32: цель 94, запас 14, ёмкость 50, sort=none  |
| PASS | seed-4000 | Бикаридин #33: цель 42, запас 3, ёмкость 100, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #34: цель 71, запас 13, ёмкость 2, sort=quantity  |
| PASS | seed-4000 | Бикаридин #35: цель 56, запас 16, ёмкость 50, sort=latest  |
| PASS | seed-4000 | Бикаридин #36: цель 99, запас 27, ёмкость 50, sort=none  |
| PASS | seed-4000 | Бикаридин #37: цель 84, запас 26, ёмкость 30, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #38: цель 26, запас 14, ёмкость 30, sort=quantity  |
| PASS | seed-4000 | Бикаридин #39: цель 44, запас 15, ёмкость 2, sort=latest  |
| PASS | seed-4000 | Бикаридин #40: цель 93, запас 29, ёмкость 3, sort=none  |
| PASS | seed-4000 | Бикаридин #41: цель 21, запас 3, ёмкость 2, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #42: цель 56, запас 16, ёмкость 100, sort=quantity  |
| PASS | seed-4000 | Бикаридин #43: цель 9, запас 4, ёмкость 10, sort=latest  |
| PASS | seed-4000 | Бикаридин #44: цель 40, запас 22, ёмкость 30, sort=none  |
| PASS | seed-4000 | Бикаридин #45: цель 88, запас 12, ёмкость 3, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #46: цель 36, запас 19, ёмкость 30, sort=quantity  |
| PASS | seed-4000 | Бикаридин #47: цель 95, запас 4, ёмкость 10, sort=latest  |
| PASS | seed-4000 | Бикаридин #48: цель 82, запас 24, ёмкость 100, sort=none  |
| PASS | seed-4000 | Бикаридин #49: цель 36, запас 13, ёмкость 100, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #50: цель 15, запас 20, ёмкость 3, sort=quantity  |
| PASS | seed-4000 | Бикаридин #51: цель 51, запас 25, ёмкость 3, sort=latest  |
| PASS | seed-4000 | Бикаридин #52: цель 60, запас 6, ёмкость 100, sort=none  |
| PASS | seed-4000 | Бикаридин #53: цель 59, запас 10, ёмкость 50, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #54: цель 19, запас 18, ёмкость 50, sort=quantity  |
| PASS | seed-4000 | Бикаридин #55: цель 47, запас 1, ёмкость 50, sort=latest  |
| PASS | seed-4000 | Бикаридин #56: цель 97, запас 19, ёмкость 10, sort=none  |
| PASS | seed-4000 | Бикаридин #57: цель 23, запас 29, ёмкость 10, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #58: цель 34, запас 17, ёмкость 50, sort=quantity  |
| PASS | seed-4000 | Бикаридин #59: цель 22, запас 14, ёмкость 30, sort=latest  |
| PASS | seed-4000 | Бикаридин #60: цель 17, запас 7, ёмкость 50, sort=none  |
| PASS | seed-4000 | Бикаридин #61: цель 70, запас 8, ёмкость 30, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #62: цель 64, запас 8, ёмкость 3, sort=quantity  |
| PASS | seed-4000 | Бикаридин #63: цель 49, запас 28, ёмкость 50, sort=latest  |
| PASS | seed-4000 | Бикаридин #64: цель 15, запас 23, ёмкость 3, sort=none  |
| PASS | seed-4000 | Бикаридин #65: цель 74, запас 20, ёмкость 100, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #66: цель 28, запас 7, ёмкость 50, sort=quantity  |
| PASS | seed-4000 | Бикаридин #67: цель 23, запас 13, ёмкость 30, sort=latest  |
| PASS | seed-4000 | Бикаридин #68: цель 29, запас 0, ёмкость 50, sort=none  |
| PASS | seed-4000 | Бикаридин #69: цель 53, запас 6, ёмкость 50, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #70: цель 60, запас 10, ёмкость 30, sort=quantity  |
| PASS | seed-4000 | Бикаридин #71: цель 70, запас 29, ёмкость 30, sort=latest  |
| PASS | seed-4000 | Бикаридин #72: цель 33, запас 21, ёмкость 50, sort=none  |
| PASS | seed-4000 | Бикаридин #73: цель 65, запас 5, ёмкость 10, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #74: цель 3, запас 28, ёмкость 2, sort=quantity  |
| PASS | seed-4000 | Бикаридин #75: цель 11, запас 27, ёмкость 2, sort=latest  |
| PASS | seed-4000 | Бикаридин #76: цель 4, запас 1, ёмкость 50, sort=none  |
| PASS | seed-4000 | Бикаридин #77: цель 51, запас 29, ёмкость 50, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #78: цель 16, запас 8, ёмкость 30, sort=quantity  |
| PASS | seed-4000 | Бикаридин #79: цель 5, запас 17, ёмкость 2, sort=latest  |
| PASS | seed-4000 | Бикаридин #80: цель 81, запас 19, ёмкость 10, sort=none  |
| PASS | seed-4000 | Бикаридин #81: цель 15, запас 21, ёмкость 100, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #82: цель 96, запас 10, ёмкость 50, sort=quantity  |
| PASS | seed-4000 | Бикаридин #83: цель 63, запас 15, ёмкость 50, sort=latest  |
| PASS | seed-4000 | Бикаридин #84: цель 47, запас 18, ёмкость 100, sort=none  |
| PASS | seed-4000 | Бикаридин #85: цель 32, запас 10, ёмкость 100, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #86: цель 57, запас 20, ёмкость 100, sort=quantity  |
| PASS | seed-4000 | Бикаридин #87: цель 22, запас 7, ёмкость 3, sort=latest  |
| PASS | seed-4000 | Бикаридин #88: цель 15, запас 18, ёмкость 100, sort=none  |
| PASS | seed-4000 | Бикаридин #89: цель 8, запас 0, ёмкость 3, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #90: цель 2, запас 24, ёмкость 3, sort=quantity  |
| PASS | seed-4000 | Бикаридин #91: цель 85, запас 27, ёмкость 3, sort=latest  |
| PASS | seed-4000 | Бикаридин #92: цель 39, запас 21, ёмкость 100, sort=none  |
| PASS | seed-4000 | Бикаридин #93: цель 73, запас 3, ёмкость 50, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #94: цель 65, запас 18, ёмкость 50, sort=quantity  |
| PASS | seed-4000 | Бикаридин #95: цель 43, запас 27, ёмкость 30, sort=latest  |
| PASS | seed-4000 | Бикаридин #96: цель 69, запас 19, ёмкость 50, sort=none  |
| PASS | seed-4000 | Бикаридин #97: цель 31, запас 9, ёмкость 10, sort=alphabetical  |
| PASS | seed-4000 | Бикаридин #98: цель 70, запас 17, ёмкость 2, sort=quantity  |
| PASS | seed-4000 | Бикаридин #99: цель 30, запас 13, ёмкость 50, sort=latest  |
| PASS | guards | Не хватает ровно 0.01u  |
| PASS | guards | Нет ни одного исходника  |
| PASS | guards | Исчезло питание  |
| PASS | guards | Нет мензурки  |
| PASS | guards | Активен режим уничтожения  |
| PASS | guards | Грязная входная ёмкость  |
| PASS | guards | Не помещается минимальная партия  |
| PASS | guards | Требуется нагрев и газовый эффект  |
| PASS | guards | Неизвестная цель среди известных запрещает частичное выполнение  |
| PASS | guards | Отрицательная цель  |
| PASS | guards | Нулевая цель  |
| PASS | guards | Неизвестная категория  |
| PASS | guards | Повторные прототипы / неизвестные reagent data  |
| PASS | guards | Неизвестный прототип в исходном составе  |
| PASS | guards | Точность меньше 0.01 не округляется молча  |
| PASS | guards | Отрицательный исходный объём  |
| PASS | guards | Объём вне диапазона FixedPoint2  |
| PASS | guards | Несуществующая кнопка 2u  |
| PASS | guards | Несуществующая строка  |
| PASS | guards | Та же сумма, но другой состав — остановка  |
| PASS | guards | Изменение сортировки после выбора строки  |
| PASS | guards | Задержавшееся подтверждение не приводит к повторному клику  |
| PASS | guards | Изменение состава между preflight и первым действием  |
| PASS | guards | Сбой после первого клика сохраняет частичное состояние и останавливает цепочку  |
| PASS | guards | Рецепт вики и игрового снимка расходятся  |
| PASS | calibration | Фиксированная тестовая разметка не изменилась  |
