/** @type {import('next').NextConfig} */
const nextConfig = {
  // Sunucusuz, statik çıktı: `next build` → out/ klasörü, uygulamaya gömülür.
  output: "export",
  images: { unoptimized: true },
  reactStrictMode: true,
  // Geliştirme sırasındaki köşe düğmesi arayüzü kapatıyor.
  devIndicators: false,
};

export default nextConfig;
