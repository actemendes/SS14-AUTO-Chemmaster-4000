# Офлайн-тесты Химмастера

Результат: **700/701 пройдено**, ошибок: 1.

SS220: `86d0f7bffb5f3f4d3ee7bef3b9080c2e37b7ec03`. Правил реакций: 345. Прототипов веществ: 453.

Игра и память игрового процесса не использовались. Калибровка проверена структурно и не изменена.

| Группа | Пройдено | Всего |
|---|---:|---:|
| calibration | 16 | 16 |
| source | 1 | 1 |
| rows | 8 | 8 |
| transfer | 5 | 5 |
| reactions | 7 | 7 |
| production | 17 | 18 |
| capacity | 6 | 6 |
| catalyst | 2 | 2 |
| selected-medicines | 257 | 257 |
| stock-matrix | 256 | 256 |
| seed-4000 | 100 | 100 |
| guards | 24 | 24 |
| manual | 1 | 1 |

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
| FAIL | production | Округление промежуточных партий 5u → 6u и возврат остатков Остаток диловена: expected 100, got 1000 |
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
| PASS | selected-medicines | PotassiumIodide: отсутствует и нет исходников  |
| PASS | selected-medicines | PotassiumIodide: уже есть ровно 10u  |
| PASS | selected-medicines | Ultravasculine: отсутствует и нет исходников  |
| PASS | selected-medicines | Ultravasculine: уже есть ровно 10u  |
| PASS | selected-medicines | Heparin: отсутствует и нет исходников  |
| PASS | selected-medicines | Heparin: уже есть ровно 10u  |
| PASS | selected-medicines | Harai: отсутствует и нет исходников  |
| PASS | selected-medicines | Harai: уже есть ровно 10u  |
| PASS | selected-medicines | Fomepizole: отсутствует и нет исходников  |
| PASS | selected-medicines | Fomepizole: уже есть ровно 10u  |
| PASS | selected-medicines | Lipozine: отсутствует и нет исходников  |
| PASS | selected-medicines | Lipozine: уже есть ровно 10u  |
| PASS | selected-medicines | Diphenylmethylamine: отсутствует и нет исходников  |
| PASS | selected-medicines | Diphenylmethylamine: уже есть ровно 10u  |
| PASS | selected-medicines | Ethyloxyephedrine: отсутствует и нет исходников  |
| PASS | selected-medicines | Ethyloxyephedrine: уже есть ровно 10u  |
| PASS | selected-medicines | Synaptizine: отсутствует и нет исходников  |
| PASS | selected-medicines | Synaptizine: уже есть ровно 10u  |
| PASS | selected-medicines | TranexamicAcid: отсутствует и нет исходников  |
| PASS | selected-medicines | TranexamicAcid: уже есть ровно 10u  |
| PASS | selected-medicines | Nicergoline: отсутствует и нет исходников  |
| PASS | selected-medicines | Nicergoline: уже есть ровно 10u  |
| PASS | selected-medicines | Arcryox: отсутствует и нет исходников  |
| PASS | selected-medicines | Arcryox: уже есть ровно 10u  |
| PASS | selected-medicines | Saline: отсутствует и нет исходников  |
| PASS | selected-medicines | Saline: уже есть ровно 10u  |
| PASS | selected-medicines | Cryptobiolin: отсутствует и нет исходников  |
| PASS | selected-medicines | Cryptobiolin: уже есть ровно 10u  |
| PASS | selected-medicines | Impedrezene: отсутствует и нет исходников  |
| PASS | selected-medicines | Impedrezene: уже есть ровно 10u  |
| PASS | selected-medicines | Ephedrine: отсутствует и нет исходников  |
| PASS | selected-medicines | Ephedrine: уже есть ровно 10u  |
| PASS | selected-medicines | Opium: отсутствует и нет исходников  |
| PASS | selected-medicines | Opium: уже есть ровно 10u  |
| PASS | selected-medicines | Stimulants: отсутствует и нет исходников  |
| PASS | selected-medicines | Stimulants: уже есть ровно 10u  |
| PASS | selected-medicines | Nocturine: отсутствует и нет исходников  |
| PASS | selected-medicines | Nocturine: уже есть ровно 10u  |
| PASS | selected-medicines | Happiness: отсутствует и нет исходников  |
| PASS | selected-medicines | Happiness: уже есть ровно 10u  |
| PASS | selected-medicines | SpaceDrugs: отсутствует и нет исходников  |
| PASS | selected-medicines | SpaceDrugs: уже есть ровно 10u  |
| PASS | selected-medicines | NorepinephricAcid: отсутствует и нет исходников  |
| PASS | selected-medicines | NorepinephricAcid: уже есть ровно 10u  |
| PASS | selected-medicines | MuteToxin: отсутствует и нет исходников  |
| PASS | selected-medicines | MuteToxin: уже есть ровно 10u  |
| PASS | selected-medicines | Desoxyephedrine: отсутствует и нет исходников  |
| PASS | selected-medicines | Desoxyephedrine: уже есть ровно 10u  |
| PASS | selected-medicines | Aglomorphine: отсутствует и нет исходников  |
| PASS | selected-medicines | Aglomorphine: уже есть ровно 10u  |
| PASS | selected-medicines | Pax: отсутствует и нет исходников  |
| PASS | selected-medicines | Pax: уже есть ровно 10u  |
| PASS | selected-medicines | Frontier: отсутствует и нет исходников  |
| PASS | selected-medicines | Frontier: уже есть ровно 10u  |
| PASS | selected-medicines | Napalm: отсутствует и нет исходников  |
| PASS | selected-medicines | Napalm: уже есть ровно 10u  |
| PASS | selected-medicines | ChlorineTrifluoride: отсутствует и нет исходников  |
| PASS | selected-medicines | ChlorineTrifluoride: уже есть ровно 10u  |
| PASS | selected-medicines | FoamingAgent: отсутствует и нет исходников  |
| PASS | selected-medicines | FoamingAgent: уже есть ровно 10u  |
| PASS | selected-medicines | Thermite: отсутствует и нет исходников  |
| PASS | selected-medicines | Thermite: уже есть ровно 10u  |
| PASS | selected-medicines | Phlogiston: отсутствует и нет исходников  |
| PASS | selected-medicines | Phlogiston: уже есть ровно 10u  |
| PASS | selected-medicines | Fluorosurfactant: отсутствует и нет исходников  |
| PASS | selected-medicines | Fluorosurfactant: уже есть ровно 10u  |
| PASS | selected-medicines | Felinase: отсутствует и нет исходников  |
| PASS | selected-medicines | Felinase: уже есть ровно 10u  |
| PASS | selected-medicines | ChloralHydrate: отсутствует и нет исходников  |
| PASS | selected-medicines | ChloralHydrate: уже есть ровно 10u  |
| PASS | selected-medicines | CorgiJuice: отсутствует и нет исходников  |
| PASS | selected-medicines | CorgiJuice: уже есть ровно 10u  |
| PASS | selected-medicines | MindbreakerToxin: отсутствует и нет исходников  |
| PASS | selected-medicines | MindbreakerToxin: уже есть ровно 10u  |
| PASS | selected-medicines | Caninase: отсутствует и нет исходников  |
| PASS | selected-medicines | Caninase: уже есть ровно 10u  |
| PASS | selected-medicines | Razorium: отсутствует и нет исходников  |
| PASS | selected-medicines | Razorium: уже есть ровно 10u  |
| PASS | selected-medicines | BuzzochloricBees: отсутствует и нет исходников  |
| PASS | selected-medicines | BuzzochloricBees: уже есть ровно 10u  |
| PASS | selected-medicines | Tazinide: отсутствует и нет исходников  |
| PASS | selected-medicines | Tazinide: уже есть ровно 10u  |
| PASS | selected-medicines | UnstableMutagen: отсутствует и нет исходников  |
| PASS | selected-medicines | UnstableMutagen: уже есть ровно 10u  |
| PASS | selected-medicines | Hemorrhinol: отсутствует и нет исходников  |
| PASS | selected-medicines | Hemorrhinol: уже есть ровно 10u  |
| PASS | selected-medicines | Lipolicide: отсутствует и нет исходников  |
| PASS | selected-medicines | Lipolicide: уже есть ровно 10u  |
| PASS | selected-medicines | Lexorin: отсутствует и нет исходников  |
| PASS | selected-medicines | Lexorin: уже есть ровно 10u  |
| PASS | selected-medicines | Licoxide: отсутствует и нет исходников  |
| PASS | selected-medicines | Licoxide: уже есть ровно 10u  |
| PASS | selected-medicines | SulfuricAcid: отсутствует и нет исходников  |
| PASS | selected-medicines | SulfuricAcid: уже есть ровно 10u  |
| PASS | selected-medicines | HeartbreakerToxin: отсутствует и нет исходников  |
| PASS | selected-medicines | HeartbreakerToxin: уже есть ровно 10u  |
| PASS | selected-medicines | PolytrinicAcid: отсутствует и нет исходников  |
| PASS | selected-medicines | PolytrinicAcid: уже есть ровно 10u  |
| PASS | selected-medicines | Fresium: отсутствует и нет исходников  |
| PASS | selected-medicines | Fresium: уже есть ровно 10u  |
| PASS | selected-medicines | FluorosulfuricAcid: отсутствует и нет исходников  |
| PASS | selected-medicines | FluorosulfuricAcid: уже есть ровно 10u  |
| PASS | selected-medicines | Ketchup: отсутствует и нет исходников  |
| PASS | selected-medicines | Ketchup: уже есть ровно 10u  |
| PASS | selected-medicines | Coldsauce: отсутствует и нет исходников  |
| PASS | selected-medicines | Coldsauce: уже есть ровно 10u  |
| PASS | selected-medicines | TableSalt: отсутствует и нет исходников  |
| PASS | selected-medicines | TableSalt: уже есть ровно 10u  |
| PASS | selected-medicines | Ketchunaise: отсутствует и нет исходников  |
| PASS | selected-medicines | Ketchunaise: уже есть ровно 10u  |
| PASS | selected-medicines | Mustard: отсутствует и нет исходников  |
| PASS | selected-medicines | Mustard: уже есть ровно 10u  |
| PASS | selected-medicines | Protein: отсутствует и нет исходников  |
| PASS | selected-medicines | Protein: уже есть ровно 10u  |
| PASS | selected-medicines | Soysauce: отсутствует и нет исходников  |
| PASS | selected-medicines | Soysauce: уже есть ровно 10u  |
| PASS | selected-medicines | Vinaigrette: отсутствует и нет исходников  |
| PASS | selected-medicines | Vinaigrette: уже есть ровно 10u  |
| PASS | selected-medicines | BbqSauce: отсутствует и нет исходников  |
| PASS | selected-medicines | BbqSauce: уже есть ровно 10u  |
| PASS | selected-medicines | EggCooked: отсутствует и нет исходников  |
| PASS | selected-medicines | EggCooked: уже есть ровно 10u  |
| PASS | selected-medicines | Vinegar: отсутствует и нет исходников  |
| PASS | selected-medicines | Vinegar: уже есть ровно 10u  |
| PASS | selected-medicines | Hotsauce: отсутствует и нет исходников  |
| PASS | selected-medicines | Hotsauce: уже есть ровно 10u  |
| PASS | selected-medicines | Mayo: отсутствует и нет исходников  |
| PASS | selected-medicines | Mayo: уже есть ровно 10u  |
| PASS | selected-medicines | Oil: отсутствует и нет исходников  |
| PASS | selected-medicines | Oil: уже есть ровно 10u  |
| PASS | selected-medicines | Left4Zed: отсутствует и нет исходников  |
| PASS | selected-medicines | Left4Zed: уже есть ровно 10u  |
| PASS | selected-medicines | Diethylamine: отсутствует и нет исходников  |
| PASS | selected-medicines | Diethylamine: уже есть ровно 10u  |
| PASS | selected-medicines | RobustHarvest: отсутствует и нет исходников  |
| PASS | selected-medicines | RobustHarvest: уже есть ровно 10u  |
| PASS | selected-medicines | EZNutrient: отсутствует и нет исходников  |
| PASS | selected-medicines | EZNutrient: уже есть ровно 10u  |
| PASS | selected-medicines | Sedin: отсутствует и нет исходников  |
| PASS | selected-medicines | Sedin: уже есть ровно 10u  |
| PASS | selected-medicines | Ammonia: отсутствует и нет исходников  |
| PASS | selected-medicines | Ammonia: уже есть ровно 10u  |
| PASS | selected-medicines | PlantBGone: отсутствует и нет исходников  |
| PASS | selected-medicines | PlantBGone: уже есть ровно 10u  |
| PASS | selected-medicines | Blood: отсутствует и нет исходников  |
| PASS | selected-medicines | Blood: уже есть ровно 10u  |
| PASS | selected-medicines | SodiumHydroxide: отсутствует и нет исходников  |
| PASS | selected-medicines | SodiumHydroxide: уже есть ровно 10u  |
| PASS | selected-medicines | Laughter: отсутствует и нет исходников  |
| PASS | selected-medicines | Laughter: уже есть ровно 10u  |
| PASS | selected-medicines | Benzene: отсутствует и нет исходников  |
| PASS | selected-medicines | Benzene: уже есть ровно 10u  |
| PASS | selected-medicines | Bleach: отсутствует и нет исходников  |
| PASS | selected-medicines | Bleach: уже есть ровно 10u  |
| PASS | selected-medicines | Ash: отсутствует и нет исходников  |
| PASS | selected-medicines | Ash: уже есть ровно 10u  |
| PASS | selected-medicines | Fersilicite: отсутствует и нет исходников  |
| PASS | selected-medicines | Fersilicite: уже есть ровно 10u  |
| PASS | selected-medicines | Lye: отсутствует и нет исходников  |
| PASS | selected-medicines | Lye: уже есть ровно 10u  |
| PASS | selected-medicines | Charcoal: отсутствует и нет исходников  |
| PASS | selected-medicines | Charcoal: уже есть ровно 10u  |
| PASS | selected-medicines | SpaceLube: отсутствует и нет исходников  |
| PASS | selected-medicines | SpaceLube: уже есть ровно 10u  |
| PASS | selected-medicines | SodiumPolyacrylate: отсутствует и нет исходников  |
| PASS | selected-medicines | SodiumPolyacrylate: уже есть ровно 10u  |
| PASS | selected-medicines | SodiumCarbonate: отсутствует и нет исходников  |
| PASS | selected-medicines | SodiumCarbonate: уже есть ровно 10u  |
| PASS | selected-medicines | ArtifactGlue: отсутствует и нет исходников  |
| PASS | selected-medicines | ArtifactGlue: уже есть ровно 10u  |
| PASS | selected-medicines | Ice: отсутствует и нет исходников  |
| PASS | selected-medicines | Ice: уже есть ровно 10u  |
| PASS | selected-medicines | Hydroxide: отсутствует и нет исходников  |
| PASS | selected-medicines | Hydroxide: уже есть ровно 10u  |
| PASS | selected-medicines | SpaceCleaner: отсутствует и нет исходников  |
| PASS | selected-medicines | SpaceCleaner: уже есть ровно 10u  |
| PASS | selected-medicines | Carpetium: отсутствует и нет исходников  |
| PASS | selected-medicines | Carpetium: уже есть ровно 10u  |
| PASS | selected-medicines | Phenol: отсутствует и нет исходников  |
| PASS | selected-medicines | Phenol: уже есть ровно 10u  |
| PASS | selected-medicines | Acetone: отсутствует и нет исходников  |
| PASS | selected-medicines | Acetone: уже есть ровно 10u  |
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
| PASS | manual | Ингредиенты внешнего этапа остаются в мензурке  |
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
