'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import {
  LayoutDashboard,
  Kanban,
  Brain,
  Settings,
  MessageSquare,
  Plug,
  Clock,
  Bot,
  Radio,
  Webhook,
} from 'lucide-react';
import { cn } from '@/lib/utils';

const navigation = [
  { name: 'Dashboard', href: '/', icon: LayoutDashboard },
  { name: 'Chat', href: '/chat', icon: MessageSquare },
  { name: 'Kanban Board', href: '/kanban', icon: Kanban },
  { name: 'Soul Config', href: '/soul', icon: Bot },
  { name: 'Memory', href: '/memory', icon: Brain },
  { name: 'Skills', href: '/skills', icon: Plug },
  { name: 'Scheduler', href: '/scheduler', icon: Clock },
  { name: 'Hooks', href: '/hooks', icon: Webhook },
  { name: 'Channels', href: '/channels', icon: Radio },
  { name: 'WhatsApp', href: '/whatsapp', icon: MessageSquare },
  { name: 'Settings', href: '/settings', icon: Settings },
];

export function Sidebar() {
  const pathname = usePathname();

  return (
    <div className="flex h-full w-64 flex-col bg-gray-900">
      <div className="flex h-16 items-center justify-center border-b border-gray-800">
        <div className="flex items-center gap-2">
          <Bot className="h-8 w-8 text-blue-400" />
          <span className="text-xl font-bold text-white">Botty Admin</span>
        </div>
      </div>
      <nav className="flex-1 space-y-1 px-2 py-4">
        {navigation.map((item) => {
          const isActive = pathname === item.href || 
            (item.href !== '/' && pathname.startsWith(item.href));
          return (
            <Link
              key={item.name}
              href={item.href}
              className={cn(
                'group flex items-center rounded-md px-2 py-2 text-sm font-medium transition-colors',
                isActive
                  ? 'bg-gray-800 text-white'
                  : 'text-gray-300 hover:bg-gray-800 hover:text-white'
              )}
            >
              <item.icon
                className={cn(
                  'mr-3 h-5 w-5 flex-shrink-0',
                  isActive ? 'text-blue-400' : 'text-gray-400 group-hover:text-gray-300'
                )}
              />
              {item.name}
            </Link>
          );
        })}
      </nav>
      <div className="border-t border-gray-800 p-4">
        <div className="flex items-center">
          <div className="h-2 w-2 rounded-full bg-green-400" />
          <span className="ml-2 text-sm text-gray-400">API Connected</span>
        </div>
      </div>
    </div>
  );
}
