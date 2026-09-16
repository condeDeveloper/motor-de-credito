# Motor de Crédito

[![CI](https://github.com/condeDeveloper/motor-de-credito/actions/workflows/ci.yml/badge.svg)](https://github.com/condeDeveloper/motor-de-credito/actions/workflows/ci.yml)

Motor de decisão de crédito pessoal em C# e .NET 8. Recebe uma proposta, pontua num scorecard, aplica a política, calcula a capacidade de pagamento, monta a oferta com plano de parcelas e devolve uma decisão explicada, com o Custo Efetivo Total.

## O que faz

- **Scorecard aditivo** com oito características (idade, renda, tipo de renda, tempo no emprego, atrasos, score de bureau, comprometimento, relacionamento), cada uma em faixas com pontos. Declarativo: calibrar é mudar uma tabela.
- **Faixas de risco A a E** com precificação por risco (taxa mensal por faixa).
- **Política**: cortes duros (restrição, idade, score mínimo, valor e prazo fora da política) recusam com motivos; faixa E recusa; faixa D vai para análise manual.
- **Capacidade de pagamento**: parcela máxima dentro do comprometimento de renda; quando o pedido não cabe, **contraproposta** com o valor reduzido ao que cabe, mantendo o prazo.
- **Planos Price e SAC** com arredondamento a centavos e fechamento exato do saldo na última parcela.
- **IOF** (0,38% fixo mais 0,0082% ao dia sobre cada amortização, limitado a 365 dias) e tarifas.
- **CET** pela taxa interna de retorno dos fluxos reais do cliente, mensal e anualizado, como manda a Resolução 3.517 do CMN.

## Rodar

```bash
dotnet run --project src/Credito.Api
```

Documentação em http://localhost:5000/docs.

```bash
curl -s localhost:5000/api/propostas/avaliar -H 'Content-Type: application/json' -d '{
  "id":"p-1","valorSolicitado":20000,"prazoMeses":24,"idade":40,"rendaMensal":12000,"despesasMensais":2000,
  "tipoRenda":"Formal","mesesNoEmprego":72,"atrasosUltimos12Meses":0,"scoreBureau":780,"restricaoAtiva":false,"clienteAtual":true
}'
# => decisão Aprovado, faixa A, taxa 1,2% a.m., oferta com 24 parcelas, IOF, CET mensal e anual, limite máximo

curl -s localhost:5000/api/simulacoes -H 'Content-Type: application/json' -d '{"valor":10000,"taxaMensal":0.01,"prazoMeses":12,"sistema":"Sac"}'
```

## Testes

```bash
dotnet test
```

Valores de referência de tabela (Price de 10.000 a 1% em 12 meses = 888,49), fechamento do saldo, SAC decrescente, TIR recuperando a taxa de um fluxo conhecido, CET igual à taxa quando não há encargos, teto do IOF, pontuação e classificação do scorecard, cortes da política, contraproposta e API.

## Arquitetura

```
src/Credito.Core
  Financeiro/   Amortizacao (Price, SAC), Cet (IOF, TIR por bisseção)
  Analise/      Proposta, Scorecard, PoliticaDeCredito
src/Credito.Api  minimal API com Swagger
tests/Credito.Tests
```

## Licença

MIT
