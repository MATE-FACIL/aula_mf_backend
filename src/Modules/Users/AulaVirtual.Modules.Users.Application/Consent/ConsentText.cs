namespace AulaVirtual.Modules.Users.Application.Consent;

public sealed record ConsentSection(string Heading, IReadOnlyList<string> Paragraphs);

/// <param name="Version">Changes whenever the text changes: everyone has to accept the new one.</param>
/// <param name="IsProvisional">True until the owner validates the text. The screens say so.</param>
public sealed record ConsentText(string Version, string Title, bool IsProvisional, IReadOnlyList<ConsentSection> Sections);

// The text a parent accepts to activate their child's account (SCRUM-38): terms of use and privacy.
//
// HOW TO CHANGE IT: edit the text here, change <see cref="ConsentText.Version"/> (any new value) and set
// IsProvisional to false once the owner has validated it. Every child whose parent accepted another version goes
// back to «pending» and the parent is asked again; the previous acceptances stay recorded.
//
// THE CURRENT TEXT IS PROVISIONAL. It was written to be able to build and test the flow and it has NOT been
// reviewed by the owner or by a lawyer: it must be validated (and the contact channel added) before opening the
// aula to families.
public static class ConsentTexts
{
    public static readonly ConsentText Current = new(
        "2026-11-provisional",
        "Términos de uso y política de privacidad",
        true,
        [
            new ConsentSection("Para qué sirve el aula", [
                "Mate Fácil Academy ofrece clases en vivo y por internet de Matemáticas y de tecnología para niños y jóvenes. En el aula virtual, tu hijo o hija practica, ve su avance y entra a sus clases, y tú puedes seguir cómo le va."
            ]),
            new ConsentSection("Qué datos usamos", [
                "Usamos los datos de tu hijo o hija que nos diste al matricularlo: nombres y apellidos, número de documento y, si lo hay, su correo.",
                "También guardamos lo que ocurre en el aula: los grupos y clases en los que está matriculado, su asistencia, sus resultados de práctica y de diagnóstico, y los mensajes que se envían por el aula."
            ]),
            new ConsentSection("Para qué los usamos", [
                "Para dar las clases, ubicar a tu hijo o hija en el nivel y el grupo que le corresponden, permitir que sus profesores vean su avance, mantenerte informado y comunicarnos contigo sobre el servicio.",
                "No los usamos para otros fines."
            ]),
            new ConsentSection("Quién puede verlos", [
                "El equipo de Mate Fácil Academy (la administración y los profesores de su grupo) y las personas de la familia vinculadas a su cuenta.",
                "Para dar las clases usamos servicios técnicos de terceros, como el alojamiento del aula y las videollamadas, que reciben solo lo necesario para funcionar."
            ]),
            new ConsentSection("Tus derechos", [
                "Puedes pedirnos ver, corregir o borrar los datos de tu hijo o hija, y retirar este consentimiento en cualquier momento, escribiéndonos por los medios de contacto de Mate Fácil Academy."
            ]),
            new ConsentSection("Uso del aula", [
                "La cuenta de cada alumno es personal y no se comparte. Te pedimos acompañar a tu hijo o hija en el uso del aula y cuidar sus datos de acceso."
            ]),
            new ConsentSection("Tu aceptación", [
                "Al aceptar declaras que eres el padre, la madre o el tutor legal de tu hijo o hija, que leíste este texto y que autorizas el uso de sus datos para lo que aquí se explica. Registramos la fecha en que aceptas."
            ])
        ]);
}
