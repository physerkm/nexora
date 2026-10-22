# Migration ve deployment süreci

## Migration'lar

Migration'lar `src/Nexora.Infrastructure/Persistence/Migrations` altında, EF Core ile üretilir ve kaynak kontrolünde tutulur.

| Migration | İçerik |
|---|---|
| `InitialCreate` | Tüm tablolar, FK'ler, indeksler (trigram GIN, kısmi, bileşik, `NULLS NOT DISTINCT`), `pg_trgm` eklentisi |
| `AuditLogImmutability` | `audit_logs` üzerinde UPDATE/DELETE/TRUNCATE engelleyen trigger |

### Yeni migration eklemek

```bash
dotnet tool restore
dotnet ef migrations add <Ad> --project src/Nexora.Infrastructure --startup-project src/Nexora.Api --output-dir Persistence/Migrations
```

Kurallar:

1. Üretilen dosyayı gözden geçirin. Özellikle kolon silme, tip daraltma ve büyük tablolara indeks eklemeye dikkat edin.
2. Büyük tablolarda indeks eklerken kilitlenmeyi önlemek için `migrationBuilder.Sql("CREATE INDEX CONCURRENTLY …")` kullanın ve o migration'ı transaction dışında çalıştırın (`suppressTransaction: true`).
3. Geriye dönük uyumsuz değişiklikleri **expand → migrate → contract** adımlarıyla yapın:
   - Yeni kolonu nullable olarak ekleyin ve kodu iki kolonla da çalışacak şekilde deploy edin.
   - Veriyi taşıyın.
   - Eski kolonu bir sonraki sürümde kaldırın.

   Böylece eski ve yeni API sürümleri aynı şemayla birlikte çalışabilir (rolling deploy).
4. CI, `dotnet ef migrations has-pending-model-changes` ile migration'ı unutulmuş bir model değişikliğinde build'i kırar.

### Migration uygulama yöntemleri

| Yöntem | Ne zaman |
|---|---|
| `dotnet Nexora.Api.dll --migrate` | Varsayılan. Migration'ları ve referans verileri (roller, ilk platform yöneticisi) uygular, sonra çıkar. Compose'daki `migrator` servisi ve CD pipeline'ı bunu kullanır. |
| İdempotent SQL script | DBA onayı gereken ortamlar. CI her build'de `artifacts/migrations.sql` üretir: `dotnet ef migrations script --idempotent -o migrations.sql`. Script yalnızca uygulanmamış migration'ları çalıştırır. |
| Migration bundle | Tek bir çalıştırılabilir dosya: `dotnet ef migrations bundle --self-contained -r linux-x64`, ardından `./efbundle --connection "<cs>"`. |
| `Database:MigrateOnStartup=true` | **Yalnızca geliştirme ortamı.** Birden çok instance aynı anda migration yapmaya çalışabileceği için üretimde kapalıdır. |

Migration veritabanında `__ef_migrations_history` tablosuna kaydedilir.

## Deployment akışı

```
push/PR ─▶ CI: build → model/migration uyumu → unit → integration (Testcontainers) → migration script → Docker build
main    ─▶ CD (önerilen):
           1. İmajları registry'ye push et (nexora-api:<sha>, nexora-web:<sha>)
           2. Veritabanı yedeği / point-in-time recovery noktası
           3. Migrator job:  docker run nexora-api:<sha> --migrate
           4. API'yi rolling olarak güncelle; /health/ready 200 olmadan trafik verme
           5. Smoke test: /health/ready, login, GET /api/v1/transactions?pageSize=1
           6. Hata olursa: önceki imaja dön (şema expand/contract ile geriye uyumlu olduğu için rollback güvenli)
```

### Sırlar (secrets)

| Değişken | Not |
|---|---|
| `Jwt__SigningKey` | En az 32 karakter rastgele değer. Secret store'dan gelmeli (GitHub Environments, Key Vault, AWS Secrets Manager). |
| `ConnectionStrings__Default` | API ve migrator için ayrı DB kullanıcıları önerilir: migrator DDL yetkili, API sadece DML. |
| `Bootstrap__PlatformAdminPassword` | Yalnızca ilk kurulumda kullanılır. İlk girişten sonra parolayı değiştirin. |

`.env` dosyası kaynak kontrolüne eklenmez (`.gitignore`). Örnek değerler için `.env.example` dosyasına bakın.

### Ölçekleme notları

- API stateless çalışır, yatay olarak çoğaltılabilir.
- Birden çok instance varken `Redis:ConnectionString` tanımlayın. Böylece access token iptal listesi, oturum durumu ve rapor cache'i tüm instance'lar arasında paylaşılır.
- Outbox dağıtıcısı `FOR UPDATE SKIP LOCKED`, bakım işi PostgreSQL advisory lock kullanır. Her ikisi de çok instance'lı ortamda güvenlidir.
- Rate limiter instance başına çalışır. Global bir limit gerekiyorsa API gateway'de (ör. nginx, Envoy) uygulayın.
- Proxy arkasında çalışırken gerçek istemci IP'si için `ForwardedHeaders` middleware'ini proxy'nin IP aralığıyla yapılandırın.

### Gözlemlenebilirlik

- **Loglar:** Production'da stdout'a JSON (CLEF) yazılır. Her satırda `CorrelationId`, `TenantId` ve `UserId` vardır. Loki, ELK veya Seq ile toplanabilir.
- **Health:** `/health/live` liveness probe, `/health/ready` readiness probe olarak kullanılır.
- **Takip edilmesi gereken metrikler:** `Failed` durumundaki outbox mesajı sayısı ve 429 oranı.
