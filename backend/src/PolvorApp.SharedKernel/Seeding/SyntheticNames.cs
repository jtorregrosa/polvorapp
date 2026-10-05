namespace PolvorApp.SharedKernel.Seeding;

/// <summary>Gender of a generated person; it follows the first name.</summary>
public enum SyntheticGender
{
    Male,
    Female,
}

/// <summary>
/// Curated name lists for the full dataset (realistic-seed-data, design D3): first names and
/// surnames common in the Alicante area, in Spanish and Valencian forms, plus a small share of names
/// common among foreign residents (who get one surname and an NIE). The maintainer's own surnames
/// are left out on purpose. A generated name may match a real person by coincidence; it is never
/// combined with a real ID, contact or image.
/// </summary>
public static class SyntheticNames
{
    internal static readonly string[] MaleFirstNames =
    [
        "Antonio", "José", "Manuel", "Francisco", "Juan", "David", "Javier", "Daniel", "Carlos", "Jesús",
        "Alejandro", "Miguel", "Rafael", "Pedro", "Pablo", "Ángel", "Sergio", "Fernando", "Jorge", "Luis",
        "Alberto", "Álvaro", "Adrián", "Diego", "Raúl", "Iván", "Rubén", "Óscar", "Andrés", "Enrique",
        "Ramón", "Joaquín", "Santiago", "Víctor", "Mario", "Hugo", "Marcos", "Jaime", "Ricardo", "José Luis",
        "Vicent", "Josep", "Joan", "Pau", "Jordi", "Jaume", "Pere", "Toni", "Xavier", "Andreu",
        "Enric", "Ferran", "Pepe", "Paco", "Marc", "Àlex", "Guillem", "Arnau", "Biel", "Josep Ramon",
        "Vicente", "Salvador", "Gaspar", "Nacho", "Quique",
    ];

    internal static readonly string[] FemaleFirstNames =
    [
        "María", "Carmen", "Ana", "Isabel", "Laura", "Cristina", "Marta", "Lucía", "Elena", "Sara",
        "Paula", "Raquel", "Rosa", "Pilar", "Teresa", "Beatriz", "Patricia", "Mónica", "Sandra", "Irene",
        "Andrea", "Alba", "Claudia", "Inmaculada", "Amparo", "Remedios", "Consuelo", "Encarna", "Mari Carmen", "Rocío",
        "Natalia", "Eva", "Maria Josep", "Neus", "Laia", "Aina", "Júlia", "Mireia", "Núria", "Carla",
        "Pepa", "Remei", "Empar", "Rosa Maria", "Montse", "Sílvia", "Clara", "Àngels", "Vicenta", "Llum",
        "Marina", "Inma", "Fina", "Lorena", "Noelia",
    ];

    internal static readonly string[] ForeignMaleFirstNames = ["Youssef", "Mohamed", "Andrei", "Ionut", "Luca"];

    internal static readonly string[] ForeignFemaleFirstNames = ["Fatima", "Ioana", "Andreea", "Yasmina", "Giulia"];

    internal static readonly string[] ForeignSurnames = ["El Amrani", "Benali", "Popescu", "Ionescu", "Rossi", "Bianchi", "Haddad", "Stan"];

    /// <summary>Common Castilian surnames.</summary>
    internal static readonly string[] SpanishSurnames =
    [
        "García", "Martínez", "López", "Sánchez", "Pérez", "Gómez", "Fernández", "Ruiz", "Navarro", "Rodríguez",
        "González", "Moreno", "Jiménez", "Torres", "Díaz", "Hernández", "Muñoz", "Romero", "Alonso", "Gil",
        "Serrano", "Molina", "Ortega", "Castillo", "Ramírez", "Rubio", "Marín", "Sanz", "Iglesias", "Medina",
        "Garrido", "Cortés", "Castro", "Lozano", "Guerrero", "Cano", "Prieto", "Méndez", "Calvo", "Gallego",
        "León", "Márquez", "Herrera", "Peña", "Flores", "Cabrera", "Campos", "Vega", "Fuentes", "Carrasco",
        "Caballero", "Reyes", "Nieto", "Aguilar", "Pascual", "Montero", "Lorenzo", "Hidalgo", "Giménez", "Ibáñez",
        "Ferrer", "Durán", "Benítez", "Mora", "Vicente", "Vargas", "Carmona", "Crespo", "Román", "Soto",
        "Sáez", "Velasco", "Moya", "Parra", "Esteban", "Bravo", "Gallardo", "Ramos", "Soriano", "Espinosa",
    ];

    /// <summary>Surnames typical of Alicante and the Valencian region.</summary>
    internal static readonly string[] ValencianSurnames =
    [
        "Sempere", "Llorens", "Gomis", "Ferrándiz", "Candela", "Mira", "Carbonell", "Baeza", "Ripoll", "Climent",
        "Esteve", "Beltrà", "Riquelme", "Asensi", "Mollà", "Moltó", "Sala", "Verdú", "Ivorra", "Castelló",
        "Puig", "Cerdà", "Boix", "Agulló", "Payá", "Seguí", "Alberola", "Lledó", "Brotons", "Botella",
        "Cremades", "Jover", "Lillo", "Llinares", "Marhuenda", "Maestre", "Miralles", "Monllor", "Orts", "Pomares",
        "Poveda", "Quiles", "Rico", "Rosselló", "Santonja", "Signes", "Sirvent", "Vives", "Alcaraz", "Amorós",
        "Aracil", "Arques", "Beneyto", "Berenguer", "Blasco", "Bernabeu", "Cantó", "Carratalá", "Català", "Coloma",
        "Doménech", "Galiana", "Giner", "Guillem", "Martí", "Mas", "Mataix", "Monzó", "Morant", "Oliver",
        "Palomares", "Pina", "Planelles", "Ribera", "Richart", "Sellés", "Valero", "Valls", "Vicedo", "Pastor",
    ];

    /// <summary>Every first name a person of <paramref name="gender"/> can have.</summary>
    public static IReadOnlyList<string> FirstNamesOf(SyntheticGender gender) => gender == SyntheticGender.Male
        ? [.. MaleFirstNames, .. ForeignMaleFirstNames]
        : [.. FemaleFirstNames, .. ForeignFemaleFirstNames];
}
