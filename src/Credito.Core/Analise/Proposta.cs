namespace Credito.Core.Analise;

public enum TipoRenda
{
    Formal,
    ServidorPublico,
    Autonomo,
    Informal,
    Aposentado,
}

/// <summary>Pedido de crédito com os dados do proponente usados pelo scorecard e pela política.</summary>
public sealed record Proposta(
    string Id,
    decimal ValorSolicitado,
    int PrazoMeses,
    int Idade,
    decimal RendaMensal,
    decimal DespesasMensais,
    TipoRenda TipoRenda,
    int MesesNoEmprego,
    int AtrasosUltimos12Meses,
    int ScoreBureau,
    bool RestricaoAtiva,
    bool ClienteAtual = false,
    decimal DividasMensaisExistentes = 0m)
{
    /// <summary>Renda disponível para novas parcelas.</summary>
    public decimal RendaLivre => Math.Max(0, RendaMensal - DespesasMensais - DividasMensaisExistentes);
}
