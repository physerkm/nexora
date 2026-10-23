# Nexora — Multi-Tenant Operations Platform

Şirketlerin şubelerini, kullanıcılarını, finansal işlemlerini, ücret kurallarını ve raporlarını tek platformdan yönettiği çok kiracılı (multi-tenant) bir SaaS uygulaması.

- **A şirketi** yalnızca kendi çalışanlarını, şubelerini ve işlemlerini görür.
- **B şirketi** yalnızca kendi verisine erişir.
- **Platform yöneticisi** tüm şirketleri listeler, yeni şirket açar, askıya alır. Belirli bir şirket adına işlem yapmak için o şirketi seçer.

| Katman | Teknoloji |
|---|---|
| Backend | ASP.NET Core 10 Minimal API, C# |
| Veritabanı | PostgreSQL 18 + EF Core 10 (Npgsql, snake_case) |
| Kimlik doğrulama | ASP.NET Core Identity (PBKDF2) + JWT + dönen refresh token |
| Validasyon | FluentValidation, endpoint filter ile |
| Cache | HybridCache (bellek L1, isteğe bağlı Redis L2) |
| Arka plan işleri | `BackgroundService` + transactional outbox + üstel geri çekilme |
| Test | xUnit, WebApplicationFactory, Testcontainers (gerçek PostgreSQL) |
| Frontend | React 19 + JavaScript + MUI 7 (DataGrid, Charts) + Vite |
| Dağıtım | Docker Compose, GitHub Actions |
| API dokümantasyonu | OpenAPI (`/openapi/v1.json`) + Swagger UI (`/swagger`) |

---

## Hızlı başlangıç

### Yerel çalıştırma (Docker gerekmez)

Gereksinimler: .NET 10 SDK, Node.js 20.19+ ve PostgreSQL 18.

**1. PostgreSQL 18'i kurun.**

```powershell
winget install PostgreSQL.PostgreSQL.18     # ya da https://www.postgresql.org/download/
```

> **Türkçe Windows'ta dikkat:** Kurulumun **Locale** ekranında `[Default locale]` yerine **English, United States** seçin.
> Windows 11 bölge ayarının adı `Turkish_Türkiye.1254` olduğundan varsayılan seçimle kurulum şu hatayla durur:
> *"The database cluster initialisation failed"* (logda: `locale name "Turkish_Türkiye.1254" contains non-ASCII characters`).
> Bu durumda PostgreSQL'i kaldırıp doğru locale ile yeniden kurun.

**2. Uygulamanın kullanıcısını ve veritabanını oluşturun.**

```powershell
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -c "CREATE ROLE nexora LOGIN PASSWORD 'nexora';" -c "CREATE DATABASE nexora OWNER nexora;"
```

Bu bilgiler yalnızca geliştirme içindir ve `appsettings.Development.json` ile eşleşir.

**3. API'yi başlatın.** Development ortamında migration'lar ve demo veri otomatik yüklenir.

```powershell
dotnet run --project src/Nexora.Api --urls http://localhost:5080
```

**4. Frontend'i başlatın** (ikinci bir terminalde). Vite, `/api` isteklerini 5080 portundaki API'ye yönlendirir.

```powershell
cd frontend
npm install
npm run dev
```

| Adres | İçerik |
|---|---|
| http://localhost:5173 | Web arayüzü |
| http://localhost:5080/swagger | Swagger UI |
| http://localhost:5080/health/ready | Hazır olma kontrolü |

### Docker Compose ile (isteğe bağlı)

```bash
cp .env.example .env            # JWT_SIGNING_KEY'i değiştirin
docker compose up --build       # web: http://localhost:3000, API: http://localhost:8080/swagger
```

Başlama sırası: `postgres` (healthy) → `migrator` (migration ve referans veri, sonra çıkar) → `api` → `web`.

### Demo hesaplar

Parolaların hepsi `Nexora#2026`.

| E-posta | Rol | Not |
|---|---|---|
| admin@nexora.io | Platform yöneticisi | Tüm şirketler |
| admin@acme.test | Şirket yöneticisi | Acme Finans A.Ş. |
| manager@acme.test | Müdür | Onay / ret |
| operator@acme.test | Operatör | Sadece "İstanbul Merkez" şubesine bağlı |
| auditor@acme.test | Denetçi | Salt okunur + denetim kayıtları |
| admin@globex.test, manager@globex.test, … | | Globex Ödeme Hizmetleri |

Her demo şirkette 1.500 işlem, 2–3 şube ve ücret kuralları bulunur.

### Büyük veri: IBM TabFormer (24 milyon işlem, isteğe bağlı)

Performansı gerçekçi bir hacimde görmek için demo şirketlere [IBM TabFormer](https://github.com/IBM/TabFormer) kredi kartı veri seti yüklenebilir. Veri sentetiktir (gerçek kişi içermez) ve Apache-2.0 lisanslıdır.

```powershell
# Önce API'yi bir kez Development ortamında başlatın (acme ve globex oluşsun), sonra:
.\tools\tabformer\import-tabformer.ps1           # indirir (~266 MB), açar (~2,2 GB) ve yükler
.\tools\tabformer\import-tabformer.ps1 -Reset    # önceki TabFormer yüklemesini silip yeniden yükler
```

Veri şöyle eşlenir:

| TabFormer | Nexora |
|---|---|
| Kart sahibi 0–999 / 1000–1999 | `acme` / `globex` (her biri ~12 milyon işlem) |
| İşyeri eyaleti / ülkesi / `ONLINE` | Şube (`TF-001`, …, ör. "ABD · CA", "Online satışlar") |
| Negatif tutar / MCC 4829 / MCC 6011 | `Refund` / `Transfer` / `Payout`, diğerleri `Payment` |
| Dolandırıcılık veya işlem hatası | `Rejected` (gerekçesiyle) |
| Son 36 saat | `Pending`, diğerleri `Approved` |
| Tarihler | En yeni işlem dün olacak şekilde kaydırılır (aradaki süreler korunur) |

Tutarlar USD'dir ve ücretler şirketin ücret kurallarıyla hesaplanır. Tutarı 0 olan satırlar atlanır. Her şirket için tek bir `import.tabformer` audit kaydı yazılır.

Betik CSV'yi `UNLOGGED` bir ara tabloya `COPY` ile alır ve tek bir `INSERT … SELECT` ile dönüştürür. Bu sırada ikincil indeksleri kaldırır ve sonda yeniden oluşturur. Bu adım hata olsa bile çalışır.

Bu makinede (16 GB RAM, SSD) toplam süre yaklaşık 1–1,5 saat sürdü. Veritabanı indekslerle birlikte ~19 GB oldu; işlem sırasında ~25 GB boş disk gerekir. İndirilen dosyalar git'e girmeyen `.data/tabformer` klasöründe kalır.

**Ölçülen API süreleri** (12,1 milyon işlemli `acme` şirketi, Development, ısınmış önbellek):

| İstek | Önce | Sonra | Ne değişti |
|---|---:|---:|---|
| Varsayılan liste (1. sayfa) | 2,0 sn | 0,09 sn | Sayım 10.000'de duruyor |
| Tutara göre sıralama | 20 sn | 0,1 sn | `(tenant_id, amount, id)` indeksi |
| Duruma göre sıralama | 23 sn | 0,1 sn | `(tenant_id, status, created_at, id)` indeksi |
| Sık geçen kelimeyle arama ("market", 1,5 milyon eşleşme) | 23 sn | 0,35 sn | Sınırlı sayım |
| Platform: tüm şirketler (24 milyon) | 19 sn | 0,08 sn | `(created_at, id)` indeksi + sınırlı sayım |
| Gösterge paneli, 90 gün (önbellek boşken) | 2,8 sn | 1,1 sn | Rapor kolonları indekste (`INCLUDE`) |
| Gösterge paneli (önbellekten) | 0,04 sn | 0,04 sn | |
| Cursor sayfalama | 0,1 sn | 0,08 sn | Zaten indeksliydi |

---

## Mimari

```
Nexora/
├── src/
│   ├── Nexora.Domain/          Varlıklar, durum makinesi, ücret hesabı. Altyapıdan bağımsız iş kuralları.
│   │   ├── Entities/           Tenant, Branch, AppUser, Transaction, FeeRule, AuditLog, RefreshToken, OutboxMessage, ...
│   │   └── Rules/              TransactionStateMachine, FeeCalculator
│   ├── Nexora.Application/     Kullanım senaryoları
│   │   ├── Services/           AuthService, TransactionService, TenantService, ReportService, ...
│   │   ├── DTOs/               İstek ve yanıt sözleşmeleri
│   │   ├── Validators/         FluentValidation kuralları
│   │   ├── Abstractions/       IApplicationDbContext, ITransactionReadRepository, IIdempotencyStore, ...
│   │   └── Common/             RequestContext (tenant/kullanıcı bağlamı), hatalar, sayfalama
│   ├── Nexora.Infrastructure/  EF Core, PostgreSQL, JWT, Redis, webhook, arka plan işleri
│   │   ├── Persistence/        DbContext, konfigürasyonlar/indeksler, migration'lar, seed
│   │   ├── Repositories/       Keyset sayfalama, rapor sorguları, idempotency deposu
│   │   ├── Security/           JWT üretimi, access token iptal listesi
│   │   └── BackgroundJobs/     Outbox dağıtıcısı, bakım işçisi (advisory lock)
│   └── Nexora.Api/             HTTP katmanı
│       ├── Endpoints/          Endpoint grupları (Auth, Platform, Tenant, Branches, Users, Fees, Transactions, Reports, Health)
│       ├── Authentication/     JWT ayarları, policy'ler, requirement handler'lar
│       ├── Middleware/         Correlation ID, istek bağlamı, global hata yönetimi
│       ├── Filters/            Validation ve Idempotency endpoint filtreleri
│       ├── RateLimiting/       Kullanıcı / IP / giriş limitleri
│       └── Program.cs          Yalnızca kompozisyon (endpoint tanımı içermez)
├── tests/
│   ├── Nexora.UnitTests/         89 test: domain kuralları, validator'lar, token, cursor, DbContext korumaları
│   └── Nexora.IntegrationTests/  80 test: gerçek API + gerçek PostgreSQL
├── frontend/                   React + MUI
├── docs/deployment.md          Migration ve deployment süreci
├── docker-compose.yml
└── .github/workflows/ci.yml
```

Bağımlılık yönü: `Api → Infrastructure → Application → Domain`.

**Bilinçli tercih:** Application katmanı EF Core'un `DbSet`'lerini `IApplicationDbContext` üzerinden kullanıyor. DbContext zaten unit-of-work ve repository işlevi gördüğü için her varlığa ayrı bir repository yazılmadı. Repository'ler yalnızca gerçekten değer kattığı yerlerde var: sağlayıcıya özgü veya performans açısından kritik sorgular (keyset sayfalama, `ILIKE`, rapor agregasyonları, `ON CONFLICT` ile idempotency).

---

## Özellikler ve nasıl çözüldüğü

### 1. Multi-tenancy ve güvenlik

**Tenant izolasyonu üç katmanlı:**

1. **Kimlikten gelen tenant.** Tenant, imzalı JWT'deki `tenant_id` claim'inden okunur. İstemci bunu değiştiremez. Normal bir kullanıcı `X-Tenant-Id` header'ıyla başka bir tenant isterse `403 cross_tenant_access` döner. Bu header'ı sadece platform yöneticisi kullanabilir.
2. **EF Core global query filter.** `Branch`, `Transaction`, `FeeRule` ve `AuditLog` tablolarında `TenantId == CurrentTenantId` filtresi her sorguya otomatik eklenir. Varsayılan davranış güvenli taraftadır: tenant yoksa ve bypass da yoksa sorgu boş döner. Başka bir tenant'ın kaydına ID ile erişim `404` verir, kaydın var olup olmadığı bile sızmaz.
3. **Yazma koruması.** `SaveChanges` içinde, ait olduğu tenant mevcut istekten farklı olan bir kayıt yazılmak istenirse işlem `ForbiddenException` ile durdurulur.

Ham SQL (rapor serisi) ve Identity kullanıcıları filtre dışında kaldığı için bunlarda tenant koşulu açıkça yazıldı ve testlerle doğrulandı.

**Kimlik doğrulama:**

- **Parolalar:** ASP.NET Core Identity ile PBKDF2-HMAC-SHA512, 210.000 iterasyon. Güçlü parola politikası ve 5 hatalı denemede 15 dakika kilit uygulanır. Bilinmeyen e-postada da sahte bir hash hesaplanır; böylece yanıt süresinden hesabın var olup olmadığı anlaşılamaz.
- **Access token:** 15 dakikalık JWT (HS256). Sadece bellekte tutulur, localStorage'a yazılmaz.
- **Refresh token:** 512 bit rastgele değer. Veritabanında yalnızca SHA-256 hash'i saklanır. Tarayıcıda `HttpOnly; Secure; SameSite=Strict` cookie içinde, `/api/v1/auth` path'iyle sınırlı tutulur.
- **Rotasyon ve çalıntı tespiti:** Her refresh tokenı tek kullanımlıktır. Daha önce kullanılmış bir token tekrar gelirse aynı girişten türeyen tüm token zinciri iptal edilir ve olay audit'e yazılır.
- **Token iptali (stateless JWT'yi iptal edilebilir yapmak):**
  - `logout`: refresh token zinciri iptal edilir, access token'ın `jti` değeri iptal listesine (dağıtık cache) girer.
  - `logout-all`, parola değişimi, kullanıcının pasife alınması, rol veya şube değişimi: security stamp yenilenir. JWT'deki `sstamp` claim'i her istekte kısa süre önbelleklenmiş değerle karşılaştırılır, eski tokenlar hemen reddedilir.
  - Şirketin askıya alınması: `ActiveTenantRequirement` geçerli token'ı olan kullanıcıları da anında keser.

**Yetkilendirme (role- ve policy-based):** Endpoint'ler rol yerine yetki adıyla korunur.

| Policy | İzin verilen roller |
|---|---|
| `platform:admin` | PlatformAdmin |
| `tenant:manage` (şube, kullanıcı, ücret, ayar yazma) | TenantAdmin, PlatformAdmin |
| `transactions:create` | Operator, Manager, TenantAdmin, PlatformAdmin |
| `transactions:review` (onay/ret) | Manager, TenantAdmin, PlatformAdmin |
| `reports:view` | Manager, TenantAdmin, Auditor, PlatformAdmin |
| `audit:view` | Auditor, TenantAdmin, PlatformAdmin |

Ek kurallar:
- **Fallback policy:** `AllowAnonymous` olmayan her endpoint kimlik doğrulama ve aktif tenant ister.
- **Resource-based `BranchAccessRequirement`:** Bir şubeye bağlı kullanıcı sadece o şubenin işlemleri üzerinde işlem yapabilir.
- **İş kuralı olarak yetki:** Operatör sadece kendi oluşturduğu bekleyen işlemi iptal edebilir. Onaylanmış işlemi iptal etmek (ters kayıt) onaycı rolü gerektirir.

### 2. Veri yönetimi ve performans

- **Sayfalama, filtre, sıralama, arama:** `GET /api/v1/transactions` şunları destekler:
  - Filtreler: `status`, `type`, `branchId`, `currency`, `from/to`, `minAmount/maxAmount`, `search`.
  - Sıralama: `sortBy` (`createdAt|amount|status|reference`), `sortDir`. Yalnızca bir indeksle karşılanan sıralamalara izin verilir; indekssiz bir kolona göre sıralamak 12 milyon satırlık bir şirkette ~20 sn sürer.
  - Offset sayfalama: `page/pageSize` (en fazla 100). Kararlı sıra için `id` tie-breaker kullanılır.
  - Toplam kayıt sayısı en fazla 10.000'e kadar sayılır (`SELECT count(*) FROM (… LIMIT 10001)`). Daha fazlası varsa `totalCount: 10000, totalCountCapped: true` döner ve arayüz "10.000+ kayıt" gösterir. Tam `COUNT(*)` büyük bir şirkette her sayfada saniyeler sürer; daha derine cursor sayfalamayla inilir.
- **Büyük tablo optimizasyonları:**
  - **Keyset (cursor) sayfalama:** `paging=cursor` ile PostgreSQL row-value karşılaştırması `(created_at, id) < (@c, @i)` kullanılır. Bu doğrudan `ix_transactions_tenant_created (tenant_id, created_at DESC, id DESC)` indeksine oturur, `OFFSET` taraması ve `COUNT(*)` gerektirmez. CSV dışa aktarımı da bu yöntemle 50.000 satıra kadar akış halinde (stream) çalışır.
  - **Arama:** `pg_trgm` GIN indeksleri sayesinde `ILIKE '%…%'` aramaları tam tablo taraması yapmaz. Kullanıcı girdisindeki `%` ve `_` karakterleri kaçışlanır.
  - **Diğer indeksler:**
    - `(tenant_id, status, created_at, id) INCLUDE (currency, amount, fee)`: Durum filtresi, duruma göre sıralama ve gösterge paneli toplamları. `INCLUDE` sayesinde rapor toplamları tabloya hiç gitmeden (index-only scan) hesaplanır.
    - `(tenant_id, amount, id)`: Tutara göre sıralama (iki yönde).
    - `(tenant_id, branch_id, created_at)`: Şube filtresi ve şube raporu.
    - `(created_at DESC, id DESC)`: Platform yöneticisinin tüm şirketleri birlikte gördüğü liste.
    - `status = 'Pending'` kısmi indeksi: Süresi dolacak işlemleri bulan iş.
    - Büyük tabloya sonradan eklenen indeksler migration içinde `CREATE INDEX CONCURRENTLY` ile oluşturulur. Böylece yazmalar durmaz.
  - **Okuma sorguları:** `AsNoTracking`, sadece gereken kolonlar, şube adları tek bir toplu sorguyla getirilir (N+1 yok).
- **Optimistic concurrency:** PostgreSQL'in `xmin` sistem kolonu sürüm (version) olarak kullanılır; ek kolon gerekmez. İstemci son gördüğü `version` değerini gönderir, araya başka bir değişiklik girmişse `409 concurrency_conflict` döner. Testte iki müdür aynı işlemi aynı anda onaylamaya/reddetmeye çalışır: biri kazanır, diğeri 409 alır.
- **Transaction yönetimi:**
  - İş değişikliği, audit kaydı ve outbox olayı tek bir `SaveChanges` çağrısıyla atomik yazılır.
  - Birden fazla `SaveChanges` gereken işlemler (tenant ile ilk yöneticisini oluşturmak, kullanıcı ile rolünü atamak) `InTransactionAsync` içinde açık bir DB transaction'ıyla yapılır. Bu, Npgsql'in yeniden deneme stratejisiyle uyumludur.

### 3. İş kuralları ve güvenilir işlemler

**Durum makinesi** (`Domain/Rules/TransactionStateMachine.cs`, tek doğruluk kaynağı):

```
Pending  ──approve──▶ Approved ──cancel (yalnız onaycı)──▶ Cancelled
   │ ├──reject──▶ Rejected
   │ ├──cancel──▶ Cancelled
   └─────expire (arka plan işi)──▶ Expired
Rejected, Cancelled, Expired: son durumlar
```

API her işlemde `allowedTransitions` alanını döner. Arayüz buna göre buton gösterir; kontrolün asıl yeri yine sunucudur. Geçersiz bir geçiş `409 invalid_state_transition` verir.

**Ek kurallar:**
- **Dört göz ilkesi:** İşlemi oluşturan kişi onu onaylayamaz veya reddedemez (`422 four_eyes_violation`).
- **Ücret hesabı:** `tutar × oran + sabit`, min/maks ile sınırlanır ve tutarı aşamaz. Para birimine özel kural genel kurala göre önceliklidir. Aynı tür ve para birimi için tek aktif kural olmasını kısmi unique indeks garanti eder (`NULLS NOT DISTINCT`).

**Idempotency (`Idempotency-Key` header):**
1. **Rezervasyon:** Filtre, anahtarı `INSERT … ON CONFLICT DO NOTHING` ile atomik olarak rezerve eder. Aynı anda gelen iki istekten yalnızca biri çalışır.
2. **Aynı anahtar, aynı içerik:** Kayıtlı yanıt tekrar döndürülür (`Idempotent-Replayed: true`), handler çalışmaz.
3. **Aynı anahtar, farklı içerik:** `422`. İlk istek hâlâ sürüyorsa `409` ve `Retry-After` döner.
4. **İş hatası:** Anahtar serbest bırakılır, istemci düzeltip aynı anahtarla tekrar deneyebilir.
5. **Son savunma hattı:** `(tenant_id, created_by_user_id, idempotency_key)` unique indeksi. Saklanan yanıt bir şekilde kaybolsa bile ikinci bir satır oluşamaz.
6. **Test:** Aynı anahtarla 12 paralel istek → veritabanında tam olarak 1 işlem.

**Değiştirilemez audit kayıtları:**
- `AuditLog` varlığının değiştirici metodu yoktur.
- `SaveChanges`, audit satırında güncelleme veya silme görürse işlemi durdurur.
- Veritabanında trigger `UPDATE`, `DELETE` ve `TRUNCATE` komutlarını reddeder; doğrudan SQL ile bağlanan biri de değiştiremez (testli).
- Her kayıt kullanıcı, IP, correlation ID ve JSON veri içerir. Başarısız girişler ve refresh token yeniden kullanımı da kayda girer.

**Tutarlı hata yanıtları:** Tüm hatalar `application/problem+json` formatında (RFC 9457) döner. İçerikte `status`, `detail`, sabit makine kodu `code` (ör. `four_eyes_violation`, `concurrency_conflict`), `traceId` ve `correlationId` bulunur. Validasyon hatalarında alan bazlı `errors` sözlüğü de gelir. Beklenmeyen hatalar loglanır ama istemciye iç ayrıntı sızdırılmaz.

### 4. Üretim ortamına hazırlık

- **Structured logging:**
  - Serilog, Production'da JSON (CLEF) formatında yazar.
  - Her log satırında `CorrelationId`, `TenantId` ve `UserId` bulunur.
  - Tek satırlık istek logu üretilir; health check istekleri gürültü yapmaz.
- **Correlation ID:** `X-Correlation-ID` header'ı alınır veya üretilir. Yanıta, loglara, audit kayıtlarına ve hata gövdelerine eklenir.
- **Health checks:** `/health/live` sadece sürecin ayakta olduğunu söyler. `/health/ready` veritabanı ve cache bağlantısını kontrol eder, JSON çıktı verir.
- **Rate limiting** (yerleşik `RateLimiter`):
  - Kullanıcı başına token bucket.
  - Anonim istekler için IP başına sabit pencere.
  - `login/refresh` için IP başına dakikada 10 istek (credential stuffing'e karşı).
  - Limit aşılınca `429` ve `Retry-After` döner.
- **Cache (ölçümle):**
  - Dashboard rapor agregasyonları `HybridCache` ile 60 saniye tutulur. Önbellek, ilgili tenant'ta bir işlem değiştiğinde tag üzerinden temizlenir.
  - Endpoint `X-Cache: HIT|MISS` ve `Server-Timing` header'ları döner; etkiyi tahmin etmek yerine ölçebilirsiniz. Yerel ölçüm (3.000 işlem): **MISS ≈ 185 ms, HIT ≈ 0,3 ms**.
  - Redis isteğe bağlıdır. Tek instance'ta bellek yeterlidir. Birden çok API instance'ı çalıştığında `Redis:ConnectionString` verilerek L2 cache, `jti` iptal listesi ve oturum durumu tüm instance'lar arasında paylaşılır (`docker compose --profile cache up`, `REDIS_CONNECTION=redis:6379`).
- **Arka plan işleri ve yeniden deneme:**
  - **Transactional outbox:** Olaylar iş değişikliğiyle aynı DB transaction'ında yazılır. `OutboxProcessor` bunları `FOR UPDATE SKIP LOCKED` ile alır; birden fazla instance aynı mesajı aynı anda işlemez.
  - **Webhook gönderimi:** HMAC-SHA256 ile imzalanır (`X-Nexora-Signature`).
  - **İki seviyeli yeniden deneme:** HTTP seviyesinde `Microsoft.Extensions.Http.Resilience` (retry, circuit breaker, timeout). Mesaj seviyesinde üstel geri çekilme + jitter (10 sn → 1 saat); 8 denemeden sonra `Failed` olarak işaretlenir.
  - **`MaintenanceWorker`:** Süresi dolan bekleyen işlemleri `Expired` yapar, eski idempotency kayıtlarını ve refresh token'ları temizler. PostgreSQL advisory lock sayesinde aynı anda yalnızca bir instance çalıştırır.
- **Docker:**
  - Çok aşamalı (multi-stage) imajlar.
  - API non-root kullanıcıyla çalışır.
  - nginx, SPA'yı sunar ve `/api` isteklerini aynı origin altında API'ye iletir (CORS gerekmez, cookie first-party kalır).

### 5. Testler ve CI/CD

```powershell
dotnet test tests/Nexora.UnitTests                 # Docker gerektirmez
dotnet test tests/Nexora.IntegrationTests          # Docker (Testcontainers, postgres:18-alpine)

# Docker yoksa: kurulu PostgreSQL'e yönlendirin. Her çalıştırmada geçici bir veritabanı açılır ve sonunda silinir.
# (PowerShell)
$env:NEXORA_TEST_POSTGRES = "Host=localhost;Port=5432;Username=postgres;Password=<postgres-parolası>;Database=postgres"
dotnet test
```

Integration testler gerçek API pipeline'ını (middleware, auth, filtreler, rate limiter) gerçek PostgreSQL üzerinde çalıştırır. Kapsanan konular:

| Alan | Örnek testler |
|---|---|
| Tenant izolasyonu | B, A'nın işlemini okuyamaz, onaylayamaz, iptal edemez, geçmişini göremez (404). Listeler, raporlar ve audit sadece kendi verisini içerir. Header ile tenant değiştirme 403. Başka tenant'ın şubesinde işlem açılamaz. |
| Yetkisiz erişim | 11 korumalı endpoint anonim istekte 401 (ProblemDetails). Rol/izin matrisi 403. Şube kısıtı. Operatör onaylayamaz. Yetki yükseltme (PlatformAdmin atama) engelli. |
| Kimlik | Rotasyon ve tekrar kullanımda zincirin iptali. Logout sonrası access token geçersiz. logout-all. Pasife alınan kullanıcının token'ı anında ölür. Kilitleme. Değiştirilmiş (tampered) token reddi. Refresh token'ın DB'de sadece hash olarak saklanması. |
| İş kuralları | Durum geçişleri, dört göz ilkesi, ücret kuralı önceliği ve tavan, stale version 409, eşzamanlı onay/ret yarışında tek kazanan, süresi dolma işi. |
| Idempotency | Replay, farklı içerik 422, anahtarın kullanıcıya özel olması, 12 paralel istekte 1 satır, hata sonrası anahtarın serbest kalması. |
| Altyapı | Audit trigger'ı (UPDATE/DELETE/TRUNCATE reddi), outbox teslimi ve backoff, advisory lock, health, correlation ID, OpenAPI, cache HIT/MISS ve invalidation, önbellek sorgularının çağıran istek bittikten sonra da çalışması, cursor ile tüm satırların tam bir kez gezilmesi. |

#### Frontend testleri

Frontend iki katmanda test edilir:

| Katman | Araçlar | Ne yapar | Süre |
|---|---|---|---|
| Bileşen testleri (34) | Vitest, React Testing Library, MSW | Uygulamayı jsdom'da çalıştırır; API'yi taklit eder | ~15 sn |
| Uçtan uca testler (9) | Playwright | Gerçek tarayıcıda gerçek API ve boş bir PostgreSQL veritabanıyla çalışır | ~1 dk |

```powershell
cd frontend
npm test              # bileşen testleri
npm run test:e2e      # uçtan uca testler (API'yi ve Vite'ı kendisi başlatır: 5180 ve 5174 portları)
```

Uçtan uca testler kendi veritabanlarını (`nexora_e2e`) kullanır. Geliştirme veritabanına dokunmazlar.
- Veritabanı her çalıştırmada boşaltılır, ardından API migration'ları uygulayıp demo veriyi yükler.
- Betik, adı `_e2e` ile bitmeyen bir veritabanını boşaltmayı reddeder.
- `nexora` kullanıcısının veritabanı oluşturma yetkisi yoksa, bu veritabanını bir kez `postgres` kullanıcısıyla oluşturun: `CREATE DATABASE nexora_e2e OWNER nexora;`
- Bağlantı ayarları `E2E_DB_HOST/PORT/USER/PASSWORD/NAME` ortam değişkenleriyle değiştirilebilir.
- Chromium indirmek yerine kurulu Edge'i kullanmak için: `$env:PW_CHANNEL = "msedge"`.

Kapsanan konular:

| Alan | Bileşen testleri | Uçtan uca testler |
|---|---|---|
| Oturum | Token'ın her isteğe eklenmesi; 401'de tek bir refresh ile yeniden deneme; eşzamanlı 401'lerin tek refresh'i paylaşması; refresh reddedilince oturumun kapanması | Hatalı parola; sayfa yenilenince oturumun sürmesi; çıkıştan sonra refresh token'ın geçersiz olması; JWT'nin JavaScript'ten okunabilir hiçbir yerde durmaması |
| Yetki | Role göre menü ve butonlar; yasak sayfaya doğrudan gidince yönlendirme; platform yöneticisinin şirket seçmeden yazamaması ve seçince `X-Tenant-Id` gönderilmesi | Operatörün menüsü ve yönlendirme; platform yöneticisinin tüm şirketleri okuyup yalnızca seçtiği şirkette yazabilmesi |
| İzolasyon | | Acme müdürü, Globex'in işlemini aramada bulamaz |
| İş akışı | Onay isteğinin `version` ve idempotency anahtarı taşıması; dört göz ilkesi; ret için gerekçe zorunluluğu; 409 çakışmasında kaydın yeniden yüklenmesi; ücret önizlemesi; hata sonrası tekrar denemede **aynı** idempotency anahtarının kullanılması; şubeye bağlı operatör | Operatörün oluşturduğu işlemi müdürün onaylaması ve geçmişte iki kullanıcının görünmesi; gerekçeli ret; dört göz ilkesi |
| Liste | "10.000+ kayıt" gösterimi; aramanın gecikmeli (debounce) tek istek atması | |

Uçtan uca testler bir hatayı da ortaya çıkardı: sayfa yenilenirken iptal edilen bir istek, HybridCache'in diğer isteklerle paylaştığı önbellek sorgusunu kapanmış bir `DbContext` ile bırakıyordu. Önbellek sorguları artık kendi DI kapsamlarında çalışıyor (`CacheScope`). Bu durum için bir regresyon testi de eklendi.

**GitHub Actions** (`.github/workflows/ci.yml`):
1. **backend:** restore, Release build, `ef migrations has-pending-model-changes` kontrolü (migration'ı unutulmuş model değişikliği build'i kırar), unit testler, integration testler (Testcontainers), idempotent migration SQL script'inin artefakt olarak üretilmesi.
2. **frontend:** `npm ci`, bileşen testleri, `npm run build`.
3. **e2e:** PostgreSQL 18 servisiyle API'yi ve Vite'ı başlatıp Playwright testlerini Chromium'da çalıştırır. Hata olursa rapor, ekran görüntüleri ve trace dosyası artefakt olarak yüklenir.
4. **docker:** API ve web imajlarının build'i, `docker compose config` doğrulaması.

Migration ve deployment adımları için: **[docs/deployment.md](docs/deployment.md)**.

---

## API özeti

Tam liste ve şemalar için Swagger UI'a bakın. Hepsi `/api/v1` altındadır.

| Metot | Yol | Açıklama |
|---|---|---|
| POST | `/auth/login`, `/auth/refresh`, `/auth/logout`, `/auth/logout-all`, `/auth/change-password` | Oturum |
| GET | `/auth/me` | Profil |
| GET/POST | `/platform/tenants`, PATCH `/platform/tenants/{id}/status` | Şirket yönetimi (platform) |
| GET/PUT | `/tenant` | Şirket ayarları, webhook |
| GET/POST/PUT | `/branches`, `/users`, `/fee-rules` | Organizasyon |
| POST | `/fees/quote` | Ücret önizleme |
| GET | `/transactions`, `/transactions/{id}`, `/transactions/{id}/history` | Sorgulama |
| POST | `/transactions` *(Idempotency-Key zorunlu)* | Oluşturma |
| POST | `/transactions/{id}/approve`, `/reject`, `/cancel` *(body: version)* | Durum geçişleri |
| GET | `/reports/summary`, `/reports/transactions.csv` | Raporlar |
| GET | `/audit-logs` | Denetim kayıtları |

Örnek:

```bash
TOKEN=$(curl -s localhost:8080/api/v1/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"operator@acme.test","password":"Nexora#2026"}' | jq -r .accessToken)

curl -s localhost:8080/api/v1/transactions -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuidgen)" \
  -d '{"branchId":"<şube-id>","type":"Payment","amount":1250.75,"currency":"TRY","counterpartyName":"Yıldız Market"}'
```

---

## Konfigürasyon

| Anahtar | Varsayılan | Açıklama |
|---|---|---|
| `ConnectionStrings:Default` | — | PostgreSQL bağlantısı |
| `Jwt:SigningKey` | — (zorunlu, ≥32 karakter) | Uygulama açılışta doğrular, eksikse başlamaz |
| `Jwt:AccessTokenMinutes` / `RefreshTokenDays` | 15 / 7 | |
| `Redis:ConnectionString` | boş | Boşsa bellek içi cache kullanılır |
| `Database:InitializeOnStartup` / `MigrateOnStartup` | true / false | Production'da migration `--migrate` ile ayrı çalıştırılır |
| `Bootstrap:PlatformAdminEmail/Password` | boş | İlk platform yöneticisi (yalnızca hiç yoksa oluşturulur) |
| `Seed:DemoData` | false | Veritabanı boşsa demo şirketleri yükler |
| `Transactions:PendingExpiryHours` | 72 | Bekleyen işlemin süresinin dolması |
| `BackgroundJobs:*` | etkin | Outbox ve bakım işlerinin aralıkları |
| `RateLimiting:*` | | Limitler |
| `Cors:AllowedOrigins` | [] | Ayrı origin'den erişen istemciler için |

Ortam değişkeni karşılığı: `Jwt__SigningKey`, `ConnectionStrings__Default` gibi.

## Bilinen sınırlar ve sonraki adımlar

- HS256 tek bir sır (secret) ile imzalıyor. Birden çok servis token doğrulayacaksa RS256/JWKS ve anahtar rotasyonuna geçilmeli.
- HybridCache'in tag temizliği yalnızca o instance'ın L1 belleğini etkiler. Diğer instance'larda bayatlık kısa L1 süresiyle (15–30 sn) sınırlı kalır.
- Tenant izolasyonu uygulama katmanında üç seviyede uygulanıyor. Daha da sıkı bir garanti için PostgreSQL Row-Level Security eklenebilir.
- Webhook URL'leri `https` ile sınırlı. Üretimde SSRF'e karşı iç ağ adreslerine giden isteklerin de engellenmesi gerekir.
- Uçtan uca testler yalnızca Chromium'da çalışıyor; Firefox ve WebKit projeleri `playwright.config.js`'e eklenebilir.
