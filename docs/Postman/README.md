# Postman API testing

Import [CommonWebPlatform.postman_collection.json](./CommonWebPlatform.postman_collection.json) into Postman. The collection covers every HTTP operation in the API's current OpenAPI document and uses `http://localhost:5247` by default.

## Sign in without 401 responses

1. Make sure `start-project.bat` is running and the API is available at `http://localhost:5247`.
2. Select the collection and edit its variables locally. Set `email` and `password` to your development account. The collection contains no password or session cookie. The local demo account previously shared with the user is `syusuf@test.com`; use the password you set locally.
3. Run **Authentication → Get a request token for cookie-authenticated state changes**. Postman retains the `CWP.AntiForgery` cookie and the collection test script saves `requestToken` as `csrfToken`.
4. Run **Authentication → Sign in and issue a short-lived HTTP-only session cookie**. The request sends `csrfToken`; Postman's cookie jar retains the `CWP.Auth` session cookie.
5. Run **Authentication → Refresh CSRF token after login**. The new request token and paired cookie are used by subsequent state-changing requests.
6. Tenant requests send the `X-Tenant-ID` collection variable automatically. Set it to a tenant where the signed-in user has an active membership. The included value is the local Demo Client tenant UUID; change it if your database differs.

Do not manually add or export authentication cookies. Postman's cookie jar handles cookies for `localhost`. A `401` usually means you need to sign in again or refresh the cookie session. A `403` means the signed-in tenant membership lacks the endpoint's permission; it is not fixed by changing the password.

## Running requests safely

- Read requests do not need a CSRF header. All state-changing requests include `X-CSRF-TOKEN: {{csrfToken}}` and require that you ran the CSRF request first.
- The collection captures IDs from successful create responses and stores them in `membershipId`, `roleId`, `roleAccessRequestId`, `notificationId`, `fileId`, and `webhookSubscriptionId` variables where applicable.
- For **Add tenant member**, first register another local account or use an existing account, then set `existingUserEmail` to its email. Membership creation does not send an invitation.
- For **Upload tenant file**, select a local PDF, PNG, or JPEG file (up to 10 MiB) in the form-data `file` field.
- Webhook creation requires the endpoint hostname to be present in the API's `Webhooks:AllowedHosts` configuration. In Development, loopback HTTP endpoints are allowed only when their host is allowlisted. The collection does not configure or contact a webhook receiver.
- Repeating create requests may return `409 Conflict` for duplicates. Approval/rejection requests require the reviewer permission and cannot be decided by the requester themselves.
- Requests that mutate local data are real: suspend/role/settings/notification/file/webhook/approval operations affect your local database or local storage. Use demo data.

## API reference

- Swagger UI: `http://localhost:5247/swagger`
- OpenAPI JSON: `http://localhost:5247/openapi/v1.json`
- Health: `GET http://localhost:5247/health` and `GET http://localhost:5247/health/ready`

The collection has one request per API operation in the current OpenAPI document, plus a second CSRF helper request for the post-login token refresh. Re-import the OpenAPI URL in Postman if you need to compare it with a later API version.
