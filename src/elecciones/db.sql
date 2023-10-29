CREATE TABLE Section (
    Id             INTEGER PRIMARY KEY,
    DistrictId     INTEGER NOT NULL,
    ProvincialId   INTEGER NOT NULL,
    SectionId      INTEGER NOT NULL,
    DistrictName   TEXT    NOT NULL,
    ProvincialName TEXT,
    SectionName    TEXT,
    UNIQUE (DistrictId, ProvincialId, SectionId)
);

CREATE TABLE Circuit (
    Id          INTEGER PRIMARY KEY,
    CircuitId   TEXT NOT NULL,
    CircuitName TEXT,
    SectionId   INTEGER REFERENCES Section(Id) ON DELETE CASCADE
);

CREATE TABLE Party (
    Id          INTEGER PRIMARY KEY,
    PartyId     INTEGER NOT NULL,
    PartyName   TEXT,
    ListId      INTEGER,
    ListName    TEXT,
    UNIQUE (PartyId, ListId)
);

CREATE TABLE Position (
    Id   INTEGER PRIMARY KEY,
    Name TEXT
)
WITHOUT ROWID;

CREATE TABLE Ballot (
    Id         INTEGER PRIMARY KEY,
    Year       INTEGER NOT NULL,
    Election   INTEGER NOT NULL,
    Circuit    INTEGER REFERENCES Circuit (Id) ON DELETE CASCADE
                       NOT NULL,
    Station    INTEGER NOT NULL,
    Electors   INTEGER NOT NULL,
    Position   INTEGER,
    Party      INTEGER REFERENCES Party (Id) ON DELETE CASCADE
                       NOT NULL,
    Kind       INTEGER NOT NULL,
    Count      INTEGER NOT NULL
);

CREATE VIEW Resultados AS
    SELECT 
        Ballot.Year AS año,
        CASE Ballot.Election 
            WHEN 0 THEN 'PASO' 
            WHEN 1 THEN 'GENERAL' 
            WHEN 2 THEN 'BALOTAJE' 
        END eleccion_tipo,
        Section.DistrictId AS distrito_id,
        Section.DistrictName AS distrito_nombre,
        Section.ProvincialId AS seccionprovincial_id,
        Section.ProvincialName AS seccionprovincial_nombre,
        Section.SectionId AS seccion_id,
        Section.SectionName AS seccion_nombre,
        Circuit.CircuitId AS circuito_id,
        Circuit.CircuitName AS circuito_nombre,
        Ballot.Station AS mesa_id,
        Ballot.Electors AS mesa_electores,
        Position.Id AS cargo_id,
        Position.Name AS cargo_nombre,
        Party.PartyId AS agrupacion_id,
        Party.PartyName AS agrupacion_nombre,
        Party.ListId AS lista_numero,
        Party.ListName AS lista_nombre,
        CASE Ballot.Kind 
            WHEN 0 THEN 'POSITIVO' 
            WHEN 1 THEN 'EN BLANCO' 
            WHEN 2 THEN 'NULO' 
            WHEN 3 THEN 'RECURRIDO' 
            WHEN 4 THEN 'IMPUGNADO' 
            WHEN 5 THEN 'COMANDO' 
        END votos_tipo,
        Ballot.Count AS votos_cantidad
    FROM Ballot 
    INNER JOIN Circuit ON Ballot.Circuit = Circuit.Id
    INNER JOIN Party ON Ballot.Party = Party.Id
    INNER JOIN Position ON Ballot.Position = Position.Id
    INNER JOIN Section ON Circuit.SectionId = Section.Id;