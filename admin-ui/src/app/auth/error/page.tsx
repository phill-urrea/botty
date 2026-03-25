"use client"

import { Bot, ShieldX } from "lucide-react"
import Link from "next/link"
import { useSearchParams } from "next/navigation"
import { Suspense } from "react"

function ErrorContent() {
  const searchParams = useSearchParams()
  const error = searchParams.get("error")

  const errorMessages: Record<string, string> = {
    AccessDenied: "Your account is not authorized to access this application.",
    Configuration: "There is a problem with the server configuration.",
    Default: "An authentication error occurred.",
  }

  const message = errorMessages[error ?? ""] ?? errorMessages.Default

  return (
    <div className="flex min-h-screen items-center justify-center bg-gray-900">
      <div className="w-full max-w-sm rounded-xl bg-gray-800 p-8 shadow-2xl">
        <div className="mb-6 flex flex-col items-center gap-3">
          <div className="flex h-16 w-16 items-center justify-center rounded-full bg-red-500/10">
            <ShieldX className="h-8 w-8 text-red-400" />
          </div>
          <div className="flex items-center gap-2">
            <Bot className="h-6 w-6 text-blue-400" />
            <h1 className="text-xl font-bold text-white">Access Denied</h1>
          </div>
        </div>
        <p className="mb-6 text-center text-sm text-gray-400">{message}</p>
        <Link
          href="/auth/signin"
          className="block w-full rounded-lg bg-blue-600 px-4 py-3 text-center text-sm font-medium text-white transition-colors hover:bg-blue-700"
        >
          Try Again
        </Link>
      </div>
    </div>
  )
}

export default function AuthErrorPage() {
  return (
    <Suspense>
      <ErrorContent />
    </Suspense>
  )
}
