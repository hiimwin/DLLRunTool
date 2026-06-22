/**
 * Nội dung handbook kiến trúc source — static, theo platform.
 * FPTCXSuite: placeholder (chưa có mô tả).
 */
window.HandbookContent = {
  loyalty: {
    title: "LoyaltyPlatform — tổng quan source",
    subtitle: "ABP / microservices · AKC.Products — đọc nhanh trước khi dev local",
    sections: [
      {
        title: "Hai repo chính (dưới loyaltyRoot)",
        body: `
          <div class="handbook-tree">
            <div class="handbook-tree-item"><code>{{loyaltyRoot}}/</code></div>
            <div class="handbook-tree-item indent-1"><strong>loyalty-platform/</strong> — solution BE (ABP, .NET)</div>
            <div class="handbook-tree-item indent-2">apps/ — AuthServer, Blazor, Web (host app)</div>
            <div class="handbook-tree-item indent-2">gateways/ — Ocelot (WebGateway, PublicWebGateway)</div>
            <div class="handbook-tree-item indent-2"><strong>services/</strong> — mỗi folder = 1 microservice (MasterData, Member, …)</div>
            <div class="handbook-tree-item indent-2">shared/ — DbMigrator, thư viện dùng chung</div>
            <div class="handbook-tree-item indent-1"><strong>loyalty-admin-portal/angular/</strong> — Admin Portal (Angular, port 4200)</div>
          </div>
          <p class="handbook-note">Tool map path qua <code>services.loyalty.json</code> + Workspace <code>loyaltyRoot</code>. Mỗi máy clone repo về chỗ khác nhau — chỉ cần trỏ đúng root.</p>
        `
      },
      {
        title: "Ví dụ: MasterDataService — cây Solution Explorer",
        body: `
          <p>Mở Visual Studio: <code>loyalty-platform/services/master.data/AKC.Products.MasterDataService.sln</code>. Cấu trúc <code>src/</code> giống hình Solution Explorer:</p>
          <div class="handbook-solution">
            <div class="handbook-solution-folder root">Solution 'AKC.Products.MasterDataService'</div>
            <div class="handbook-solution-folder">└ src/</div>
            <div class="handbook-solution-item cs">Application — *AppService.cs (logic nghiệp vụ)</div>
            <div class="handbook-solution-item cs">Application.Contracts — I*AppService, *Dto (interface + DTO)</div>
            <div class="handbook-solution-item cs">Domain — Entity, rule, I*Repository</div>
            <div class="handbook-solution-item cs">Domain.Shared — enum, constant dùng chung</div>
            <div class="handbook-solution-item cs">EntityFrameworkCore — DbContext, migration, *Repository.cs</div>
            <div class="handbook-solution-item cs">HttpApi — *Controller.cs (REST endpoint)</div>
            <div class="handbook-solution-item cs">HttpApi.Client — proxy gọi API từ service khác</div>
            <div class="handbook-solution-item host">HttpApi.Host — chạy local (dotnet dll --urls) · port 44364</div>
            <div class="handbook-solution-item host">Job.Host / Job.Quartz — background job (port 44365)</div>
          </div>
          <div class="handbook-callout">
            <strong>Gợi ý nhớ nhanh:</strong> sửa API → <code>HttpApi</code> + <code>Application</code> · sửa DB/entity → <code>Domain</code> + <code>EntityFrameworkCore</code> · chạy service → <code>HttpApi.Host</code>.
          </div>
        `
      },
      {
        title: "Một request đi qua class nào? (ví dụ Offer)",
        body: `
          <div class="handbook-layer-flow">
            <span class="handbook-layer-chip">Portal / Postman</span>
            <span class="handbook-layer-arrow">→</span>
            <span class="handbook-layer-chip">WebGateway :44325</span>
            <span class="handbook-layer-arrow">→</span>
            <span class="handbook-layer-chip">OfferController</span>
            <span class="handbook-layer-arrow">→</span>
            <span class="handbook-layer-chip">OfferAppService</span>
            <span class="handbook-layer-arrow">→</span>
            <span class="handbook-layer-chip">IOfferRepository</span>
            <span class="handbook-layer-arrow">→</span>
            <span class="handbook-layer-chip">SQL Server</span>
          </div>
          <table class="handbook-table">
            <thead><tr><th>Lớp</th><th>File ví dụ</th><th>Làm gì</th></tr></thead>
            <tbody>
              <tr>
                <td><strong>HttpApi</strong></td>
                <td><code>Offers/OfferController.cs</code></td>
                <td>Nhận HTTP <code>GET/POST api/master-data-service/offer</code>, gọi AppService</td>
              </tr>
              <tr>
                <td><strong>Application</strong></td>
                <td><code>Offers/OfferAppService.cs</code></td>
                <td>Validate, orchestration, map Entity ↔ DTO</td>
              </tr>
              <tr>
                <td><strong>Application.Contracts</strong></td>
                <td><code>IOfferAppService</code>, <code>OfferDto</code></td>
                <td>Contract cho controller &amp; client khác gọi</td>
              </tr>
              <tr>
                <td><strong>Domain</strong></td>
                <td><code>Offers/Offer.cs</code></td>
                <td>Entity + business rule (tier, campaign, …)</td>
              </tr>
              <tr>
                <td><strong>EntityFrameworkCore</strong></td>
                <td><code>OfferRepository.cs</code>, <code>DbContext</code></td>
                <td>Query/insert/update DB, migration</td>
              </tr>
              <tr>
                <td><strong>HttpApi.Host</strong></td>
                <td><code>Program.cs</code>, <code>appsettings.json</code></td>
                <td>Khởi động Kestrel, DI, middleware — <strong>entry Run trong tool</strong></td>
              </tr>
            </tbody>
          </table>
          <p class="handbook-note">Các service khác (Member, Transaction, Identity, …) <strong>cùng pattern</strong>, chỉ đổi tên namespace <code>AKC.Products.&lt;Tên&gt;Service</code>.</p>
        `
      },
      {
        title: "Luồng request (dev local)",
        body: `
          <div class="handbook-flow">
            <div class="handbook-flow-step">Browser / Portal <span class="handbook-flow-port">:4200</span></div>
            <div class="handbook-flow-arrow">↓ HTTPS</div>
            <div class="handbook-flow-step">WebGateway <span class="handbook-flow-port">:44325</span> <span class="handbook-flow-tag">Ocelot</span></div>
            <div class="handbook-flow-arrow">↓ route theo ocelot.json</div>
            <div class="handbook-flow-step">Microservice *.HttpApi.Host <span class="handbook-flow-port">443xx</span></div>
            <div class="handbook-flow-arrow">↓</div>
            <div class="handbook-flow-step">SQL Server · Redis · RabbitMQ</div>
          </div>
          <p class="handbook-note">Auth/OAuth: <strong>AuthServer</strong> (:44322) — token trước khi gọi API qua gateway. Public API có thể qua <strong>PublicWebGateway</strong> (:44353).</p>
        `
      },
      {
        title: "Bảng project ABP — vai trò từng lớp",
        body: `
          <table class="handbook-table">
            <thead><tr><th>Project</th><th>Chứa gì</th><th>Khi nào mở</th></tr></thead>
            <tbody>
              <tr><td><code>*.HttpApi.Host</code></td><td>Startup, config, middleware</td><td>Chạy/debug service local</td></tr>
              <tr><td><code>*.HttpApi</code></td><td>Controller REST (<code>[Route]</code>, <code>[HttpGet]</code>)</td><td>Thêm/sửa endpoint API</td></tr>
              <tr><td><code>*.Application</code></td><td><code>*AppService</code> — hàm nghiệp vụ</td><td>Logic chính, gọi repo/cache/event</td></tr>
              <tr><td><code>*.Application.Contracts</code></td><td>Interface + DTO input/output</td><td>Định nghĩa contract API</td></tr>
              <tr><td><code>*.Domain</code></td><td>Entity, domain service, rule</td><td>Đổi model nghiệp vụ</td></tr>
              <tr><td><code>*.EntityFrameworkCore</code></td><td>DbContext, migration, repository impl</td><td>Đổi schema DB, query</td></tr>
              <tr><td><code>*.Job.*</code></td><td>Quartz job, worker nền</td><td>Sync/schedule chạy background</td></tr>
            </tbody>
          </table>
          <p class="handbook-note">Config runtime: <code>appsettings.json</code> trong Host hoặc <code>bin/Debug/netX.0</code> sau build. Gateway route: <code>ocelot.json</code>.</p>
        `
      },
      {
        title: "Services trong loyalty-platform/services/",
        body: `
          <table class="handbook-table handbook-table-compact">
            <thead><tr><th>Folder</th><th>Domain chính</th><th>Port (local)</th></tr></thead>
            <tbody>
              <tr><td>identity</td><td>User, role, OpenIddict client</td><td>44388</td></tr>
              <tr><td>saas</td><td>Multi-tenant SaaS</td><td>44381</td></tr>
              <tr><td>administration</td><td>Permission, setting, audit</td><td>44367</td></tr>
              <tr><td>master.data</td><td>Master data, attribute, LOV, offer</td><td>44364</td></tr>
              <tr><td>member</td><td>Member profile, tier</td><td>44371</td></tr>
              <tr><td>transaction</td><td>Điểm, giao dịch loyalty</td><td>44368</td></tr>
              <tr><td>customer.journey</td><td>Mission, offer, journey</td><td>44363</td></tr>
              <tr><td>sync.data</td><td>Đồng bộ dữ liệu</td><td>44370</td></tr>
              <tr><td>product</td><td>Sản phẩm loyalty</td><td>44361</td></tr>
              <tr><td>segment</td><td>Phân khúc member</td><td>44366</td></tr>
              <tr><td>GDPR</td><td>Privacy / GDPR</td><td>44362</td></tr>
              <tr><td>attributes</td><td>Attribute engine</td><td>—</td></tr>
              <tr><td>loyalty.experience</td><td>Experience layer</td><td>—</td></tr>
            </tbody>
          </table>
          <p class="handbook-note">Các <strong>*Job</strong> host (MasterData Job, Member Job, …) chạy background worker — port 44365–44375 trong tool.</p>
        `
      },
      {
        title: "Apps & Gateways",
        body: `
          <table class="handbook-table handbook-table-compact">
            <thead><tr><th>Thành phần</th><th>Path (tương đối)</th><th>Port</th></tr></thead>
            <tbody>
              <tr><td>AuthServer</td><td>apps/auth-server/src/AKC.Products.AuthServer</td><td>44322</td></tr>
              <tr><td>WebGateway</td><td>gateways/web/src/AKC.Products.WebGateway</td><td>44325</td></tr>
              <tr><td>PublicWebGateway</td><td>gateways/web-public/src/AKC.Products.PublicWebGateway</td><td>44353</td></tr>
              <tr><td>DbMigrator</td><td>shared/AKC.Products.DbMigrator</td><td>chạy 1 lần</td></tr>
              <tr><td>Admin Portal</td><td>loyalty-admin-portal/angular</td><td>4200 (npm)</td></tr>
            </tbody>
          </table>
        `
      },
      {
        title: "Thứ tự chạy local (gợi ý)",
        body: `
          <ol class="handbook-ol">
            <li><strong>Redis</strong> — cache, distributed lock</li>
            <li><strong>AuthServer</strong> — OAuth/OpenIddict</li>
            <li><strong>Identity</strong> → <strong>Saas</strong> — nền tảng tenant/user</li>
            <li>Các microservice domain (Administration, MasterData, Member, …)</li>
            <li><strong>WebGateway</strong> / PublicWebGateway</li>
            <li><strong>Admin Portal</strong> (Angular) — trỏ env.js về gateway</li>
          </ol>
          <p class="handbook-note"><strong>DbMigrator</strong>: migrate DB khi schema đổi — mặc định khóa Run trong tool. RabbitMQ cần chạy nếu service dùng queue.</p>
        `
      },
      {
        title: "Phụ thuộc hạ tầng",
        body: `
          <ul class="handbook-ul">
            <li><strong>SQL Server</strong> — mỗi service có connection string trong appsettings (global config tool ghi chung)</li>
            <li><strong>Redis</strong> — session, cache (redis-server.exe)</li>
            <li><strong>RabbitMQ</strong> — event bus ABP (localhost:5672)</li>
            <li><strong>HTTPS dev</strong> — cert local; portal :4200 self-signed (browser warning bình thường)</li>
          </ul>
        `
      }
    ]
  },

  fptcx: {
    empty: true,
    title: "FPTCXSuite",
    message: "Handbook kiến trúc FPTCXSuite sẽ được bổ sung sau.",
    hint: "Hiện tool chỉ mô tả chi tiết LoyaltyPlatform. Chuyển platform Loyalty ở góc trên để xem handbook."
  }
};
