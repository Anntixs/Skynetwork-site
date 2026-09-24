namespace SkyNetwork.Site.Data;

/// <summary>
/// Countries members choose from (registration and profile). The English name is stored; the list is shown in the
/// visitor's language. Russian names typed before the list existed are still recognised.
/// </summary>
public static class Countries
{
    private static readonly (string En, string Ru)[] Names =
    [
        ("Russia", "Россия"), ("Afghanistan", "Афганистан"), ("Albania", "Албания"), ("Algeria", "Алжир"), ("Andorra", "Андорра"),
        ("Angola", "Ангола"), ("Antigua and Barbuda", "Антигуа и Барбуда"), ("Argentina", "Аргентина"), ("Armenia", "Армения"),
        ("Australia", "Австралия"), ("Austria", "Австрия"), ("Azerbaijan", "Азербайджан"), ("Bahamas", "Багамы"), ("Bahrain", "Бахрейн"),
        ("Bangladesh", "Бангладеш"), ("Barbados", "Барбадос"), ("Belarus", "Беларусь"), ("Belgium", "Бельгия"), ("Belize", "Белиз"),
        ("Benin", "Бенин"), ("Bhutan", "Бутан"), ("Bolivia", "Боливия"), ("Bosnia and Herzegovina", "Босния и Герцеговина"),
        ("Botswana", "Ботсвана"), ("Brazil", "Бразилия"), ("Brunei", "Бруней"), ("Bulgaria", "Болгария"), ("Burkina Faso", "Буркина-Фасо"),
        ("Burundi", "Бурунди"), ("Cambodia", "Камбоджа"), ("Cameroon", "Камерун"), ("Canada", "Канада"), ("Cape Verde", "Кабо-Верде"),
        ("Central African Republic", "ЦАР"), ("Chad", "Чад"), ("Chile", "Чили"), ("China", "Китай"), ("Colombia", "Колумбия"),
        ("Comoros", "Коморы"), ("Congo", "Конго"), ("DR Congo", "ДР Конго"), ("Costa Rica", "Коста-Рика"), ("Côte d’Ivoire", "Кот-д’Ивуар"),
        ("Croatia", "Хорватия"), ("Cuba", "Куба"), ("Cyprus", "Кипр"), ("Czechia", "Чехия"), ("Denmark", "Дания"), ("Djibouti", "Джибути"),
        ("Dominica", "Доминика"), ("Dominican Republic", "Доминиканская Республика"), ("East Timor", "Восточный Тимор"),
        ("Ecuador", "Эквадор"), ("Egypt", "Египет"), ("El Salvador", "Сальвадор"), ("Equatorial Guinea", "Экваториальная Гвинея"),
        ("Eritrea", "Эритрея"), ("Estonia", "Эстония"), ("Eswatini", "Эсватини"), ("Ethiopia", "Эфиопия"), ("Fiji", "Фиджи"),
        ("Finland", "Финляндия"), ("France", "Франция"), ("Gabon", "Габон"), ("Gambia", "Гамбия"), ("Georgia", "Грузия"),
        ("Germany", "Германия"), ("Ghana", "Гана"), ("Greece", "Греция"), ("Grenada", "Гренада"), ("Guatemala", "Гватемала"),
        ("Guinea", "Гвинея"), ("Guinea-Bissau", "Гвинея-Бисау"), ("Guyana", "Гайана"), ("Haiti", "Гаити"), ("Honduras", "Гондурас"),
        ("Hungary", "Венгрия"), ("Iceland", "Исландия"), ("India", "Индия"), ("Indonesia", "Индонезия"), ("Iran", "Иран"), ("Iraq", "Ирак"),
        ("Ireland", "Ирландия"), ("Israel", "Израиль"), ("Italy", "Италия"), ("Jamaica", "Ямайка"), ("Japan", "Япония"),
        ("Jordan", "Иордания"), ("Kazakhstan", "Казахстан"), ("Kenya", "Кения"), ("Kiribati", "Кирибати"), ("North Korea", "КНДР"),
        ("South Korea", "Южная Корея"), ("Kuwait", "Кувейт"), ("Kyrgyzstan", "Киргизия"), ("Laos", "Лаос"), ("Latvia", "Латвия"),
        ("Lebanon", "Ливан"), ("Lesotho", "Лесото"), ("Liberia", "Либерия"), ("Libya", "Ливия"), ("Liechtenstein", "Лихтенштейн"),
        ("Lithuania", "Литва"), ("Luxembourg", "Люксембург"), ("Madagascar", "Мадагаскар"), ("Malawi", "Малави"), ("Malaysia", "Малайзия"),
        ("Maldives", "Мальдивы"), ("Mali", "Мали"), ("Malta", "Мальта"), ("Marshall Islands", "Маршалловы Острова"),
        ("Mauritania", "Мавритания"), ("Mauritius", "Маврикий"), ("Mexico", "Мексика"), ("Micronesia", "Микронезия"),
        ("Moldova", "Молдова"), ("Monaco", "Монако"), ("Mongolia", "Монголия"), ("Montenegro", "Черногория"), ("Morocco", "Марокко"),
        ("Mozambique", "Мозамбик"), ("Myanmar", "Мьянма"), ("Namibia", "Намибия"), ("Nauru", "Науру"), ("Nepal", "Непал"),
        ("Netherlands", "Нидерланды"), ("New Zealand", "Новая Зеландия"), ("Nicaragua", "Никарагуа"), ("Niger", "Нигер"),
        ("Nigeria", "Нигерия"), ("North Macedonia", "Северная Македония"), ("Norway", "Норвегия"), ("Oman", "Оман"),
        ("Pakistan", "Пакистан"), ("Palau", "Палау"), ("Palestine", "Палестина"), ("Panama", "Панама"),
        ("Papua New Guinea", "Папуа — Новая Гвинея"), ("Paraguay", "Парагвай"), ("Peru", "Перу"), ("Philippines", "Филиппины"),
        ("Poland", "Польша"), ("Portugal", "Португалия"), ("Qatar", "Катар"), ("Romania", "Румыния"), ("Rwanda", "Руанда"),
        ("Saint Kitts and Nevis", "Сент-Китс и Невис"), ("Saint Lucia", "Сент-Люсия"),
        ("Saint Vincent and the Grenadines", "Сент-Винсент и Гренадины"), ("Samoa", "Самоа"), ("San Marino", "Сан-Марино"),
        ("São Tomé and Príncipe", "Сан-Томе и Принсипи"), ("Saudi Arabia", "Саудовская Аравия"), ("Senegal", "Сенегал"),
        ("Serbia", "Сербия"), ("Seychelles", "Сейшелы"), ("Sierra Leone", "Сьерра-Леоне"), ("Singapore", "Сингапур"),
        ("Slovakia", "Словакия"), ("Slovenia", "Словения"), ("Solomon Islands", "Соломоновы Острова"), ("Somalia", "Сомали"),
        ("South Africa", "ЮАР"), ("South Sudan", "Южный Судан"), ("Spain", "Испания"), ("Sri Lanka", "Шри-Ланка"), ("Sudan", "Судан"),
        ("Suriname", "Суринам"), ("Sweden", "Швеция"), ("Switzerland", "Швейцария"), ("Syria", "Сирия"), ("Tajikistan", "Таджикистан"),
        ("Tanzania", "Танзания"), ("Thailand", "Таиланд"), ("Togo", "Того"), ("Tonga", "Тонга"), ("Trinidad and Tobago", "Тринидад и Тобаго"),
        ("Tunisia", "Тунис"), ("Turkey", "Турция"), ("Turkmenistan", "Туркменистан"), ("Tuvalu", "Тувалу"), ("Uganda", "Уганда"),
        ("Ukraine", "Украина"), ("United Arab Emirates", "ОАЭ"), ("United Kingdom", "Великобритания"), ("United States", "США"),
        ("Uruguay", "Уругвай"), ("Uzbekistan", "Узбекистан"), ("Vanuatu", "Вануату"), ("Vatican City", "Ватикан"),
        ("Venezuela", "Венесуэла"), ("Vietnam", "Вьетнам"), ("Yemen", "Йемен"), ("Zambia", "Замбия"), ("Zimbabwe", "Зимбабве"),
    ];

    private static readonly Dictionary<string, (string En, string Ru)> ByName =
        Names.SelectMany(n => new[] { (Key: n.En, n), (Key: n.Ru, n) }).ToDictionary(x => x.Key, x => x.n, StringComparer.OrdinalIgnoreCase);

    /// <summary>(stored value, shown name) in the visitor's language: Russia first, then alphabetical.</summary>
    public static IEnumerable<(string Value, string Name)> List(bool russian) =>
        Names.Take(1).Concat(Names.Skip(1).OrderBy(n => russian ? n.Ru : n.En, StringComparer.InvariantCulture))
            .Select(n => (n.En, russian ? n.Ru : n.En));

    /// <summary>The stored (English) name of a country from the list, in either language; null when unknown.</summary>
    public static string? Normalize(string? name) => name != null && ByName.TryGetValue(name.Trim(), out var n) ? n.En : null;

    /// <summary>A stored country for display; anything not in the list is shown as it was typed.</summary>
    public static string Display(string? stored, bool russian) =>
        stored != null && ByName.TryGetValue(stored.Trim(), out var n) ? (russian ? n.Ru : n.En) : stored ?? "";
}
