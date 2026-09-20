using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bfocus.Internal;

namespace Bfocus;

/// <summary>
/// Pessoas dos clientes (quem abre o widget/portal) — <c>client.People</c>. Escopos: <c>customers:read</c> /
/// <c>customers:write</c>. O id da pessoa é o <c>user.externalId</c> que o seu backend assina no widget.
/// </summary>
public sealed class PeopleResource
{
    /// <summary>Máximo de itens por chamada de <see cref="BatchAsync"/> (limite da API; a SDK não divide sozinha).</summary>
    public const int MaxBatchSize = Batch.MaxItems;

    private readonly BfocusHttp _http;

    internal PeopleResource(BfocusHttp http)
    {
        _http = http;
        Identifiers = new PersonIdentifiersResource(http);
    }

    /// <summary>Identificadores extras das pessoas (ids de outros sistemas seus ligados ao mesmo cadastro).</summary>
    public PersonIdentifiersResource Identifiers { get; }

    /// <summary>
    /// Cria ou atualiza a pessoa do cliente (<c>PUT /customers/{external_id}/people/{person_external_id}</c>). Só os
    /// campos informados mudam. O e-mail (ou o telefone) acha a pessoa que já chegou por e-mail ou por outro sistema —
    /// ela é adotada, nunca duplicada; a mesma pessoa em outro cliente é LIGADA a ele também. <c>Access = true</c> devolve o
    /// acesso retirado por <see cref="DeleteAsync"/>. <see cref="PersonUpsert.CustomFields"/> é a exceção: a lista
    /// enviada SUBSTITUI a lista inteira de campos personalizados da pessoa (campo que ficar de fora é removido).
    /// <see cref="PersonUpsert.Clear"/> APAGA contato (<c>email</c>/<c>phone</c>) — e não se confunde com
    /// <see cref="PatchRequest.ClearFields"/>, que manda <c>null</c>, o que em pessoa quer dizer "não mexe".
    /// </summary>
    /// <param name="customerExternalId">Id do cliente no seu sistema.</param>
    /// <param name="personExternalId">Id da pessoa no seu sistema (sem <c>:</c> se ela vai abrir o widget).</param>
    /// <param name="person">Campos a gravar.</param>
    /// <param name="options">Opções da chamada (ex.: <c>IdempotencyKey</c>).</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>A pessoa gravada, com <see cref="PersonUpsertResult.Status"/> (<c>created</c>, <c>updated</c> ou <c>unchanged</c>).</returns>
    /// <exception cref="ConflictException">
    /// Ex.: <c>PERSON_EMAIL_STAFF</c> (e-mail de alguém da sua equipe), <c>PERSON_EMAIL_TAKEN</c> /
    /// <c>PERSON_PHONE_TAKEN</c> (o <see cref="BfocusException.ErrorData"/> diz de quem é o contato) ou
    /// <c>PERSON_CONTACT_OTHER_CUSTOMER</c> (recusa definitiva: a pessoa é de outro cliente); ou
    /// <c>PERSON_CLEAR_NOT_OWN_RECORD</c>, quando um <see cref="PersonUpsert.Clear"/> chega por um identificador extra.
    /// </exception>
    /// <exception cref="ValidationException"><c>PERSON_CLEAR_FIELD_INVALID</c>: campo fora da lista aceita em <see cref="PersonUpsert.Clear"/>.</exception>
    public Task<PersonUpsertResult> UpsertAsync(string customerExternalId, string personExternalId, PersonUpsert person, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (person is null)
        {
            throw new ArgumentNullException(nameof(person));
        }

        var body = new JsonObject { ["person"] = person.ToJsonObject() }.ToJsonString(BfocusJson.Options);
        return _http.RequestAsync<PersonUpsertResult>(HttpMethod.Put, PersonPath(customerExternalId, personExternalId), null, body, options, cancellationToken);
    }

    /// <summary>Lista as pessoas do cliente (<c>GET /customers/{external_id}/people</c>), com e sem acesso.</summary>
    /// <param name="customerExternalId">Id do cliente no seu sistema.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>As pessoas.</returns>
    public Task<IReadOnlyList<Person>> ListAsync(string customerExternalId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<IReadOnlyList<Person>>(HttpMethod.Get, CustomersResource.CustomerPath(customerExternalId) + "/people", null, null, options, cancellationToken);

    /// <summary>
    /// Retira o acesso da pessoa (<c>DELETE /customers/{external_id}/people/{person_external_id}</c>). Ela continua no
    /// histórico; <see cref="UpsertAsync"/> com <c>Access = true</c> devolve o acesso.
    /// </summary>
    /// <param name="customerExternalId">Id do cliente no seu sistema.</param>
    /// <param name="personExternalId">Id da pessoa no seu sistema.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>A pessoa, com <see cref="Person.Access"/> = <c>false</c>.</returns>
    /// <exception cref="NotFoundException"><c>PERSON_NOT_FOUND</c>.</exception>
    public Task<PersonRevokeResult> DeleteAsync(string customerExternalId, string personExternalId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<PersonRevokeResult>(HttpMethod.Delete, PersonPath(customerExternalId, personExternalId), null, null, options, cancellationToken);

    /// <summary>
    /// Cria ou atualiza até <see cref="MaxBatchSize"/> pessoas numa chamada (<c>POST /people/batch</c>), de clientes
    /// quaisquer. A SDK NÃO divide: acima de <see cref="MaxBatchSize"/> lança <see cref="ArgumentException"/> antes de
    /// qualquer requisição — fatie do seu lado (o <see cref="BatchItemResult.Index"/> é a posição no lote enviado).
    /// Lista vazia devolve o resultado zerado sem requisição. Um item com erro não desfaz os outros.
    /// </summary>
    /// <param name="items">Pessoas (cada uma com <see cref="PersonBatchItem.CustomerExternalId"/> e <see cref="PersonBatchItem.ExternalId"/>).</param>
    /// <param name="options">Opções da chamada (ex.: <c>IdempotencyKey</c> — um lote é uma chamada).</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>Um resultado por item + o resumo.</returns>
    /// <exception cref="ArgumentException">Mais de <see cref="MaxBatchSize"/> itens, item <c>null</c> ou sem os ids.</exception>
    public Task<BatchResult> BatchAsync(IEnumerable<PersonBatchItem> items, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = Batch.Take(items, "people.batch", nameof(items));
        if (list.Count == 0)
        {
            return Task.FromResult(new BatchResult());
        }

        var array = new JsonArray();
        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i] ?? throw new ArgumentException($"A pessoa #{i} do lote é null.", nameof(items));
            if (string.IsNullOrEmpty(item.CustomerExternalId))
            {
                throw new ArgumentException($"A pessoa #{i} do lote não tem CustomerExternalId.", nameof(items));
            }

            if (string.IsNullOrEmpty(item.ExternalId))
            {
                throw new ArgumentException($"A pessoa #{i} do lote não tem ExternalId.", nameof(items));
            }

            var person = item.ToJsonObject();
            person.Remove("customer_external_id");
            array.Add(new JsonObject { ["customer_external_id"] = item.CustomerExternalId, ["person"] = person });
        }

        return _http.RequestAsync<BatchResult>(HttpMethod.Post, "/people/batch", null, Batch.Body(array), options, cancellationToken);
    }

    private static string PersonPath(string customerExternalId, string personExternalId) =>
        CustomersResource.CustomerPath(customerExternalId) + "/people/" + PathSegment.Encode(personExternalId, nameof(personExternalId));
}

/// <summary>
/// Identificadores extras de uma pessoa — <c>client.People.Identifiers</c>. Liga o id de outro sistema seu ao mesmo
/// cadastro (idempotente); id que já é de outro cadastro → <see cref="ConflictException"/> <c>IDENTIFIER_IN_USE</c>.
/// </summary>
public sealed class PersonIdentifiersResource
{
    private readonly BfocusHttp _http;

    internal PersonIdentifiersResource(BfocusHttp http) => _http = http;

    /// <summary>
    /// Todos os identificadores da pessoa (<c>GET /people/{person_external_id}/identifiers</c>): o principal
    /// (<see cref="PersonIdentifiers.ExternalId"/>) e os extras. Aceita no caminho o principal OU qualquer um dos
    /// extras. Escopo <c>customers:read</c>.
    /// <para>
    /// É a fonte de verdade para RECONCILIAR: <see cref="PeopleResource.ListAsync"/> mostra só o identificador
    /// principal, então um id que virou extra some de lá sem ter sumido do cadastro — e, sem esta leitura, era preciso
    /// ESCREVER (tentar um <see cref="AddAsync"/>) para descobrir o que tinha acontecido.
    /// </para>
    /// </summary>
    /// <param name="personExternalId">Id (principal ou extra) da pessoa no seu sistema.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>O identificador principal + todos os extras.</returns>
    /// <exception cref="NotFoundException"><c>PERSON_NOT_FOUND</c>.</exception>
    public Task<PersonIdentifiers> ListAsync(string personExternalId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<PersonIdentifiers>(HttpMethod.Get, "/people/" + PathSegment.Encode(personExternalId, nameof(personExternalId)) + "/identifiers", null, null, options, cancellationToken);

    /// <summary>Liga o identificador extra à pessoa (<c>PUT /people/{person_external_id}/identifiers/{extra_id}</c>). Idempotente.</summary>
    /// <param name="personExternalId">Id (principal ou extra) da pessoa no seu sistema.</param>
    /// <param name="extraId">Identificador extra.</param>
    /// <param name="label">Rótulo livre (ex.: nome do sistema). <c>null</c> = sem corpo.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>Os identificadores da pessoa.</returns>
    /// <exception cref="ConflictException"><c>IDENTIFIER_IN_USE</c> (o id já é de outro cadastro).</exception>
    public Task<PersonIdentifiers> AddAsync(string personExternalId, string extraId, string? label = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<PersonIdentifiers>(HttpMethod.Put, IdentifierPath(personExternalId, extraId), null, IdentifierBody.Label(label), options, cancellationToken);

    /// <summary>Desliga o identificador extra da pessoa (<c>DELETE /people/{person_external_id}/identifiers/{extra_id}</c>).</summary>
    /// <param name="personExternalId">Id (principal ou extra) da pessoa no seu sistema.</param>
    /// <param name="extraId">Identificador extra.</param>
    /// <param name="options">Opções da chamada.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    /// <returns>Os identificadores restantes da pessoa.</returns>
    /// <exception cref="NotFoundException"><c>IDENTIFIER_NOT_FOUND</c>.</exception>
    public Task<PersonIdentifiers> RemoveAsync(string personExternalId, string extraId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<PersonIdentifiers>(HttpMethod.Delete, IdentifierPath(personExternalId, extraId), null, null, options, cancellationToken);

    private static string IdentifierPath(string personExternalId, string extraId) =>
        "/people/" + PathSegment.Encode(personExternalId, nameof(personExternalId)) + "/identifiers/" + PathSegment.Encode(extraId, nameof(extraId));
}
