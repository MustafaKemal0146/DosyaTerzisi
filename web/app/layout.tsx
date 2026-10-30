import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Dosya Terzisi",
  description: "Dosya ve klasörleri toplu olarak yeniden adlandırın.",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="tr">
      <body>{children}</body>
    </html>
  );
}
